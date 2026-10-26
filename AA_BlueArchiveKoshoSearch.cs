// BlueArchiveKoshoSearch - portable single-exe app (WinForms, .NET Framework 4.x)
// Build with BuildExe.bat (uses csc.exe bundled with Windows).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("AA BlueArchive Kosho Search")]
[assembly: System.Reflection.AssemblyProduct("AA BlueArchive Kosho Search")]
[assembly: System.Reflection.AssemblyDescription("Unofficial fan-made tool for Blue Archive")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace BAKosho
{
    class Seg
    {
        public string Text = "";
        public string Marker;
        public string Note;
    }

    class Entry
    {
        public string Target = "";
        public List<Seg> Calls = new List<Seg>();
    }

    class Chara
    {
        public string Name = "";
        public List<string> Icons = new List<string>();
        public string School = "";
        public string Group = "";
        public List<Seg> Self = new List<Seg>();
        public List<Entry> Rows = new List<Entry>();
    }

    static class Wiki
    {
        public const string Url = "https://bluearchive.wikiru.jp/?%E3%82%AD%E3%83%A3%E3%83%A9%E5%91%BC%E7%A7%B0%E8%A1%A8";
        public const string Origin = "https://bluearchive.wikiru.jp/";

        public static string StripTags(string s)
        {
            if (s == null) return "";
            s = Regex.Replace(s, "<[^>]+>", " ");
            s = WebUtility.HtmlDecode(s);
            s = s.Replace('\u00a0', ' ');
            return Regex.Replace(s, "\\s+", " ").Trim();
        }

        public static string Norm(string s)
        {
            if (s == null) return "";
            try { s = s.Normalize(NormalizationForm.FormKC); } catch { }
            s = s.ToLowerInvariant();
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (ch >= '\u3041' && ch <= '\u3096') sb.Append((char)(ch + 0x60));
                else if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            }
            return sb.ToString();
        }

        static string CleanHeading(string inner)
        {
            inner = Regex.Replace(inner, "<a[^>]*>([\\s\\S]*?)</a>", delegate(Match m)
            {
                string t = StripTags(m.Groups[1].Value);
                if (t.Length == 0 || t == "\u2020" || t == "\u2191" || string.Equals(t, "edit", StringComparison.OrdinalIgnoreCase)) return " ";
                return m.Groups[1].Value;
            }, RegexOptions.IgnoreCase);
            string s = StripTags(inner);
            s = Regex.Replace(s, "\\S+\\.(?:png|jpe?g|gif|webp)", " ", RegexOptions.IgnoreCase);
            return Regex.Replace(s, "\\s+", " ").Trim();
        }

        static Dictionary<string, string> ParseNotes(string html)
        {
            Dictionary<string, string> notes = new Dictionary<string, string>();
            MatchCollection ms = Regex.Matches(html,
                "<a[^>]*id=\"notefoot_(\\d+)\"[^>]*>[\\s\\S]*?</a>([\\s\\S]*?)(?=<a[^>]*id=\"notefoot_|</li|</ul|</ol|</div)",
                RegexOptions.IgnoreCase);
            foreach (Match m in ms)
            {
                string key = "*" + m.Groups[1].Value;
                string txt = StripTags(m.Groups[2].Value);
                if (txt.Length > 0 && !notes.ContainsKey(key)) notes[key] = txt;
            }
            return notes;
        }

        static List<Seg> ExtractSegments(string cellHtml, Dictionary<string, string> notes)
        {
            List<Seg> segs = new List<Seg>();
            string[] parts = Regex.Split(cellHtml, "<br\\s*/?\\s*>", RegexOptions.IgnoreCase);
            foreach (string part in parts)
            {
                string p = part;
                string marker = null;
                Match mm = Regex.Match(p, "<a[^>]*href=\"[^\"]*#notefoot_(\\d+)\"[^>]*>[\\s\\S]*?</a>", RegexOptions.IgnoreCase);
                if (mm.Success)
                {
                    marker = "*" + mm.Groups[1].Value;
                    p = p.Remove(mm.Index, mm.Length);
                }
                string t = StripTags(p);
                if (t.Length == 0 && marker == null) continue;
                Seg s = new Seg();
                s.Text = t;
                s.Marker = marker;
                if (marker != null && notes.ContainsKey(marker)) s.Note = notes[marker];
                segs.Add(s);
            }
            return segs;
        }

        class TableCtx
        {
            public List<List<string>> Rows = new List<List<string>>();
            public List<string> CurRow;
            public int CellStart = -1;
        }

        public static List<Chara> Parse(string html)
        {
            Dictionary<string, string> notes = ParseNotes(html);
            int start = 0, end = html.Length;
            int bi = html.IndexOf("id=\"body\"", StringComparison.Ordinal);
            if (bi >= 0) start = bi;
            int mi = html.IndexOf("id=\"menubar\"", StringComparison.Ordinal);
            if (mi > start) end = mi;
            string region = html.Substring(start, end - start);

            Dictionary<string, Chara> byKey = new Dictionary<string, Chara>();
            List<string> order = new List<string>();
            string curH2 = "", curH3 = "", lastH4 = "";
            List<TableCtx> stack = new List<TableCtx>();
            int skipUntil = -1;

            Regex tagRe = new Regex("<(/?)(h2|h3|h4|table|tr|td|th)\\b[^>]*>", RegexOptions.IgnoreCase);
            Match m = tagRe.Match(region);
            while (m.Success)
            {
                if (m.Index >= skipUntil)
                {
                    bool closing = m.Groups[1].Value == "/";
                    string tag = m.Groups[2].Value.ToLowerInvariant();
                    if (tag == "h2" || tag == "h3" || tag == "h4")
                    {
                        if (!closing && stack.Count == 0)
                        {
                            int endIdx = region.IndexOf("</" + tag, m.Index, StringComparison.OrdinalIgnoreCase);
                            if (endIdx >= 0)
                            {
                                string t = CleanHeading(region.Substring(m.Index + m.Length, endIdx - m.Index - m.Length));
                                if (tag == "h2") { curH2 = t; curH3 = ""; lastH4 = ""; }
                                else if (tag == "h3") { curH3 = t; lastH4 = ""; }
                                else lastH4 = t;
                                skipUntil = endIdx;
                            }
                        }
                    }
                    else if (tag == "table")
                    {
                        if (!closing)
                        {
                            if (stack.Count > 0) CloseCell(stack[stack.Count - 1], region, m.Index);
                            stack.Add(new TableCtx());
                        }
                        else if (stack.Count > 0)
                        {
                            TableCtx ctx = stack[stack.Count - 1];
                            stack.RemoveAt(stack.Count - 1);
                            CloseCell(ctx, region, m.Index);
                            ProcessTable(ctx.Rows, notes, byKey, order, curH2, curH3, lastH4);
                        }
                    }
                    else if (stack.Count > 0)
                    {
                        TableCtx top = stack[stack.Count - 1];
                        if (tag == "tr")
                        {
                            CloseCell(top, region, m.Index);
                            if (!closing) { top.CurRow = new List<string>(); top.Rows.Add(top.CurRow); }
                            else top.CurRow = null;
                        }
                        else // td / th
                        {
                            CloseCell(top, region, m.Index);
                            if (!closing)
                            {
                                if (top.CurRow == null) { top.CurRow = new List<string>(); top.Rows.Add(top.CurRow); }
                                top.CellStart = m.Index + m.Length;
                            }
                        }
                    }
                }
                m = m.NextMatch();
            }

            List<Chara> result = new List<Chara>();
            foreach (string k in order) result.Add(byKey[k]);
            return result;
        }

        static void CloseCell(TableCtx ctx, string region, int idx)
        {
            if (ctx.CellStart >= 0 && ctx.CurRow != null)
            {
                ctx.CurRow.Add(region.Substring(ctx.CellStart, idx - ctx.CellStart));
            }
            ctx.CellStart = -1;
        }

        static void ProcessTable(List<List<string>> rows, Dictionary<string, string> notes,
            Dictionary<string, Chara> byKey, List<string> order, string curH2, string curH3, string lastH4)
        {
            int headerIdx = -1;
            for (int i = 0; i < rows.Count && i < 3; i++)
            {
                List<string> plain = new List<string>();
                foreach (string c in rows[i]) plain.Add(Regex.Replace(StripTags(c), "\\s+", ""));
                if (plain.Count >= 2 && plain.Count <= 6 && plain.Contains("\u76f8\u624b") && plain.Contains("\u547c\u79f0"))
                {
                    headerIdx = i;
                    break;
                }
            }
            if (headerIdx < 0) return;

            Chara cur = null;
            for (int i = headerIdx + 1; i < rows.Count; i++)
            {
                List<string> cells = rows[i];
                string targetHtml, callHtml;
                if (cells.Count >= 3)
                {
                    string ownerCell = cells[0];
                    List<string> icons = new List<string>();
                    foreach (Match imTag in Regex.Matches(ownerCell, "<img[^>]*>", RegexOptions.IgnoreCase))
                    {
                        string[] attrNames = new string[] { "data-src", "data-original", "data-lazy-src", "src" };
                        foreach (string an in attrNames)
                        {
                            string av = AttrVal(imTag.Value, an);
                            if (av == null) continue;
                            av = av.Trim();
                            if (av.Length == 0) continue;
                            if (av.StartsWith("data:")) continue;
                            if (Regex.IsMatch(av, "loading|spacer|blank|dummy|pixel|1x1", RegexOptions.IgnoreCase)) continue;
                            if (!icons.Contains(av)) icons.Add(av);
                        }
                    }
                    string ownerName = "";
                    foreach (Match am in Regex.Matches(ownerCell, "<a[^>]*>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase))
                    {
                        string t = StripTags(am.Groups[1].Value);
                        if (t.Length > 0 && !Regex.IsMatch(t, "\\.(?:png|jpe?g|gif|webp)$", RegexOptions.IgnoreCase)) ownerName = t;
                    }
                    if (ownerName.Length == 0)
                    {
                        ownerName = Regex.Replace(StripTags(ownerCell), "\\S+\\.(?:png|jpe?g|gif|webp)", " ", RegexOptions.IgnoreCase);
                        ownerName = Regex.Replace(ownerName, "\\s+", " ").Trim();
                    }
                    string name = lastH4.Length > 0 ? lastH4 : ownerName;
                    if (name.Length == 0) { cur = null; continue; }
                    cur = GetOrCreate(byKey, order, name, curH2, curH3);
                    if (cur.Icons.Count == 0 && icons.Count > 0) cur.Icons = icons;
                    targetHtml = cells[1];
                    callHtml = cells[2];
                }
                else if (cells.Count == 2)
                {
                    if (cur == null && lastH4.Length > 0) cur = GetOrCreate(byKey, order, lastH4, curH2, curH3);
                    targetHtml = cells[0];
                    callHtml = cells[1];
                }
                else continue;
                if (cur == null) continue;

                string cleanedTarget = Regex.Replace(targetHtml, "<a[^>]*href=\"[^\"]*#notefoot_\\d+\"[^>]*>[\\s\\S]*?</a>", " ", RegexOptions.IgnoreCase);
                string target = StripTags(cleanedTarget);
                if (target.Length == 0) continue;
                List<Seg> calls = ExtractSegments(callHtml, notes);
                if (target == "\u81ea\u5206")
                {
                    if (cur.Self.Count == 0) cur.Self = calls;
                }
                else
                {
                    Entry en = new Entry();
                    en.Target = target;
                    en.Calls = calls;
                    cur.Rows.Add(en);
                }
            }
        }

        static Chara GetOrCreate(Dictionary<string, Chara> byKey, List<string> order, string name, string school, string group)
        {
            string key = Norm(name);
            Chara c;
            if (!byKey.TryGetValue(key, out c))
            {
                c = new Chara();
                c.Name = name;
                c.School = school;
                c.Group = group;
                byKey[key] = c;
                order.Add(key);
            }
            if (c.School.Length == 0) c.School = school;
            if (c.Group.Length == 0) c.Group = group;
            return c;
        }

        static string AttrVal(string tag, string name)
        {
            Match m = Regex.Match(tag, "(?<![\\w-])" + Regex.Escape(name) + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return m.Groups[1].Value + m.Groups[2].Value;
        }

        public static string ResolveUrl(string src)
        {
            if (src == null) return null;
            string s = WebUtility.HtmlDecode(src).Trim();
            if (s.StartsWith("http://") || s.StartsWith("https://")) return s;
            if (s.StartsWith("//")) return "https:" + s;
            try { return new Uri(new Uri(Origin), s).ToString(); }
            catch { return null; }
        }

        public static string JoinSegs(List<Seg> segs)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Seg s in segs)
            {
                if (s.Text.Length == 0) continue;
                if (sb.Length > 0) sb.Append("\u3001");
                sb.Append(s.Text);
            }
            return sb.ToString();
        }
    }
}

// ---- UI part (appended to src_main.cs by the build preparation step) ----
namespace BAKosho
{
    class CharItem
    {
        public Chara C;
        public CharItem(Chara c) { C = c; }
        public override string ToString()
        {
            string meta = C.School;
            if (C.Group.Length > 0) meta = meta.Length > 0 ? meta + " / " + C.Group : C.Group;
            return meta.Length > 0 ? C.Name + "\u3000\uff08" + meta + "\uff09" : C.Name;
        }
    }

    class OrgItem
    {
        public string Key = "";
        public string Name = "";
        public int Count;
        public override string ToString()
        {
            return Name + "\u3000\uff08" + Count.ToString() + "\u4eba\u304c\u8a00\u53ca\uff09";
        }
    }

    class MainForm : Form
    {
        static string BaseDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        static string CachePath { get { return Path.Combine(BaseDir, "kosho_cache.html"); } }
        static string SettingsPath { get { return Path.Combine(BaseDir, "kosho_settings.txt"); } }
        static string IconDir { get { return Path.Combine(BaseDir, "kosho_icons"); } }

        List<Chara> _chars = new List<Chara>();
        Dictionary<string, Chara> _charIndex = new Dictionary<string, Chara>();
        List<object> _charItems = new List<object>();
        List<object> _orgItems = new List<object>();
        Dictionary<string, Image> _iconCache = new Dictionary<string, Image>();
        Dictionary<string, Image> _fallbackCache = new Dictionary<string, Image>();
        Chara _selA, _selB;
        OrgItem _selO;
        bool _guard;
        bool _busy;
        bool _restoring;

        Label _status;
        Button _btnReload, _btnImport, _btnSwap;
        ComboBox _cbA, _cbB, _cbO;
        PictureBox _picA, _picB;
        Label _selfA, _selfB;
        Label _headAB, _headBA;
        FlowLayoutPanel _flowAB, _flowBA, _orgFlow;
        ToolTip _tip = new ToolTip();

        static readonly Color ChipBack = Color.FromArgb(224, 239, 252);
        static readonly Color ChipFore = Color.FromArgb(21, 79, 128);
        static readonly Color MutedFore = Color.FromArgb(120, 128, 136);

        public MainForm()
        {
            Text = "AA \u30d6\u30eb\u30a2\u30ab \u30ad\u30e3\u30e9\u547c\u79f0\u691c\u7d22";
            Font = new Font("Yu Gothic UI", 9.5F);
            BackColor = Color.White;
            ClientSize = new Size(920, 838);
            MinimumSize = new Size(760, 678);
            _tip.AutoPopDelay = 20000;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _status = new Label();
            _status.Location = new Point(14, 14);
            _status.Size = new Size(540, 20);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _status.AutoEllipsis = true;
            _status.ForeColor = MutedFore;
            _status.Text = "\u8d77\u52d5\u4e2d\u2026";
            Controls.Add(_status);

            _btnReload = new Button();
            _btnReload.Text = "\u6700\u65b0\u30c7\u30fc\u30bf\u3092\u518d\u53d6\u5f97";
            _btnReload.Size = new Size(150, 28);
            _btnReload.Location = new Point(574, 10);
            _btnReload.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnReload.Click += delegate { RefreshData(true); };
            Controls.Add(_btnReload);

            _btnImport = new Button();
            _btnImport.Text = "\u4fdd\u5b58\u3057\u305fHTML\u3092\u8aad\u307f\u8fbc\u3080";
            _btnImport.Size = new Size(170, 28);
            _btnImport.Location = new Point(736, 10);
            _btnImport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnImport.Click += delegate { ImportHtml(); };
            Controls.Add(_btnImport);

            GroupBox grpPair = new GroupBox();
            grpPair.Text = "\u30ad\u30e3\u30e9\u30af\u30bf\u30fc\u540c\u58eb\u306e\u547c\u79f0\u691c\u7d22";
            grpPair.Location = new Point(14, 46);
            grpPair.Size = new Size(892, 306);
            grpPair.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(grpPair);

            _picA = MakePic(new Point(16, 30));
            grpPair.Controls.Add(_picA);
            _cbA = MakeCombo(new Point(82, 34), 250);
            grpPair.Controls.Add(_cbA);
            _selfA = MakeSelfLabel(new Point(82, 64), 250);
            grpPair.Controls.Add(_selfA);

            _btnSwap = new Button();
            _btnSwap.Text = "\u21c4";
            _btnSwap.Size = new Size(42, 30);
            _btnSwap.Location = new Point(346, 33);
            _btnSwap.Click += delegate { SwapSelection(); };
            _tip.SetToolTip(_btnSwap, "2\u4eba\u3092\u5165\u308c\u66ff\u3048");
            grpPair.Controls.Add(_btnSwap);

            _picB = MakePic(new Point(402, 30));
            grpPair.Controls.Add(_picB);
            _cbB = MakeCombo(new Point(468, 34), 250);
            grpPair.Controls.Add(_cbB);
            _selfB = MakeSelfLabel(new Point(468, 64), 250);
            grpPair.Controls.Add(_selfB);

            _headAB = MakeHead(new Point(16, 106));
            grpPair.Controls.Add(_headAB);
            _flowAB = MakeFlow(new Point(16, 128), new Size(860, 66));
            grpPair.Controls.Add(_flowAB);

            _headBA = MakeHead(new Point(16, 204));
            grpPair.Controls.Add(_headBA);
            _flowBA = MakeFlow(new Point(16, 226), new Size(860, 66));
            grpPair.Controls.Add(_flowBA);

            GroupBox grpOrg = new GroupBox();
            grpOrg.Text = "\u7d44\u7e54\u30fb\u30b0\u30eb\u30fc\u30d7\u30fbNPC\u306a\u3069\u3078\u306e\u547c\u79f0\u691c\u7d22";
            grpOrg.Location = new Point(14, 360);
            grpOrg.Size = new Size(892, 408);
            grpOrg.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(grpOrg);

            Label credit = new Label();
            credit.Location = new Point(14, 776);
            credit.Size = new Size(892, 48);
            credit.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            credit.ForeColor = MutedFore;
            credit.Font = new Font("Yu Gothic UI", 8.25F);
            credit.TextAlign = ContentAlignment.MiddleCenter;
            credit.AutoEllipsis = true;
            credit.Text = "\u672c\u30bd\u30d5\u30c8\u30a6\u30a7\u30a2\u306f\u975e\u516c\u5f0f\u306e\u30d5\u30a1\u30f3\u30e1\u30a4\u30c9\u30c4\u30fc\u30eb\u3067\u3059\uff08\u516c\u5f0f\u304a\u3088\u3073\u5404\u904b\u55b6\u3068\u306f\u95a2\u4fc2\u3042\u308a\u307e\u305b\u3093\uff09\u3002\n\u300e\u30d6\u30eb\u30fc\u30a2\u30fc\u30ab\u30a4\u30d6\u300f \u958b\u767a\uff1aNEXON Games \uff0f \u904b\u55b6\uff1aYostar\u3000\uff5c\u3000\u30c7\u30fc\u30bf\u51fa\u5178\uff1a\u30d6\u30eb\u30fc\u30a2\u30fc\u30ab\u30a4\u30d6\uff08\u30d6\u30eb\u30a2\u30ab\uff09\u653b\u7565Wiki\u300c\u30ad\u30e3\u30e9\u547c\u79f0\u8868\u300d\uff08Wiki\u7ba1\u7406\u4eba\u30fb\u7de8\u96c6\u8005\u306e\u7686\u69d8\u304c\u7ba1\u7406\u30fb\u66f4\u65b0\uff09\n\u30b2\u30fc\u30e0\u306e\u958b\u767a\u30fb\u904b\u55b6\u30c1\u30fc\u30e0\u3001\u304a\u3088\u3073Wiki\u306e\u7ba1\u7406\u30fb\u66f4\u65b0\u306b\u643a\u308f\u308b\u7686\u69d8\u306b\u5fc3\u3088\u308a\u611f\u8b1d\u3044\u305f\u3057\u307e\u3059\u3002";
            Controls.Add(credit);

            _cbO = MakeCombo(new Point(16, 30), 330);
            grpOrg.Controls.Add(_cbO);
            Label hint = new Label();
            hint.AutoSize = true;
            hint.Location = new Point(356, 34);
            hint.ForeColor = MutedFore;
            hint.Text = "\u4f8b: \u4fbf\u5229\u5c4b68\u3001\u30b2\u30fc\u30e0\u958b\u767a\u90e8\u3001\u5148\u751f \u306a\u3069";
            grpOrg.Controls.Add(hint);

            _orgFlow = new FlowLayoutPanel();
            _orgFlow.Location = new Point(16, 66);
            _orgFlow.Size = new Size(860, 326);
            _orgFlow.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _orgFlow.FlowDirection = FlowDirection.TopDown;
            _orgFlow.WrapContents = false;
            _orgFlow.AutoScroll = true;
            _orgFlow.BackColor = Color.FromArgb(250, 251, 253);
            grpOrg.Controls.Add(_orgFlow);

            WireCombo(_cbA, delegate { return _charItems; }, delegate(object o)
            {
                _selA = o == null ? null : ((CharItem)o).C;
                SaveSel(); RenderPair();
            });
            WireCombo(_cbB, delegate { return _charItems; }, delegate(object o)
            {
                _selB = o == null ? null : ((CharItem)o).C;
                SaveSel(); RenderPair();
            });
            WireCombo(_cbO, delegate { return _orgItems; }, delegate(object o)
            {
                _selO = o as OrgItem;
                SaveSel(); RenderOrg();
            });

            Shown += delegate { StartInit(); };
        }

        static PictureBox MakePic(Point loc)
        {
            PictureBox p = new PictureBox();
            p.Location = loc;
            p.Size = new Size(56, 56);
            p.SizeMode = PictureBoxSizeMode.Zoom;
            p.BackColor = Color.FromArgb(240, 244, 248);
            return p;
        }

        ComboBox MakeCombo(Point loc, int width)
        {
            ComboBox cb = new ComboBox();
            cb.Location = loc;
            cb.Width = width;
            cb.DropDownStyle = ComboBoxStyle.DropDown;
            cb.AutoCompleteMode = AutoCompleteMode.None;
            cb.DropDownWidth = width + 170;
            cb.MaxDropDownItems = 16;
            return cb;
        }

        Label MakeSelfLabel(Point loc, int width)
        {
            Label l = new Label();
            l.Location = loc;
            l.Size = new Size(width, 20);
            l.AutoEllipsis = true;
            l.ForeColor = MutedFore;
            return l;
        }

        Label MakeHead(Point loc)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Location = loc;
            l.Font = new Font(Font, FontStyle.Bold);
            return l;
        }

        FlowLayoutPanel MakeFlow(Point loc, Size size)
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Location = loc;
            f.Size = size;
            f.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            f.AutoScroll = true;
            f.WrapContents = true;
            f.BackColor = Color.FromArgb(250, 251, 253);
            return f;
        }

        // ---------- combo wiring with incremental filtering ----------
        void WireCombo(ComboBox cb, Func<List<object>> master, Action<object> onCommit)
        {
            cb.SelectionChangeCommitted += delegate
            {
                object item = cb.SelectedItem;
                BeginInvoke((MethodInvoker)delegate { CommitItem(cb, item, onCommit); });
            };
            cb.TextUpdate += delegate
            {
                if (_guard) return;
                _guard = true;
                try
                {
                    string txt = cb.Text;
                    object[] items = FilterItems(master(), txt);
                    cb.BeginUpdate();
                    cb.Items.Clear();
                    cb.Items.AddRange(items);
                    cb.EndUpdate();
                    if (items.Length > 0 && txt.Length > 0 && !cb.DroppedDown)
                    {
                        cb.DroppedDown = true;
                        Cursor.Current = Cursors.Default;
                    }
                    if (cb.Text != txt) cb.Text = txt;
                    cb.SelectionStart = txt.Length;
                    cb.SelectionLength = 0;
                    if (txt.Length == 0) onCommit(null);
                }
                finally { _guard = false; }
            };
            cb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    object hit = ResolveByText(master(), cb.Text);
                    if (hit == null)
                    {
                        object[] items = FilterItems(master(), cb.Text);
                        if (items.Length > 0 && cb.Text.Length > 0) hit = items[0];
                    }
                    if (hit != null) CommitItem(cb, hit, onCommit);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            };
            cb.Leave += delegate
            {
                object hit = ResolveByText(master(), cb.Text);
                if (hit != null) CommitItem(cb, hit, onCommit);
            };
        }

        void CommitItem(ComboBox cb, object item, Action<object> onCommit)
        {
            if (item == null) return;
            _guard = true;
            try
            {
                if (cb.DroppedDown) cb.DroppedDown = false;
                string name = item is CharItem ? ((CharItem)item).C.Name : ((OrgItem)item).Name;
                cb.Text = name;
                cb.SelectionStart = name.Length;
                cb.SelectionLength = 0;
            }
            finally { _guard = false; }
            onCommit(item);
        }

        static string ItemName(object o)
        {
            CharItem ci = o as CharItem;
            if (ci != null) return ci.C.Name;
            OrgItem oi = o as OrgItem;
            if (oi != null) return oi.Name;
            return o.ToString();
        }

        static object[] FilterItems(List<object> master, string text)
        {
            string key = Wiki.Norm(text);
            if (key.Length == 0) return master.ToArray();
            List<object> pre = new List<object>();
            List<object> mid = new List<object>();
            foreach (object o in master)
            {
                string nk = Wiki.Norm(ItemName(o));
                if (nk.StartsWith(key)) pre.Add(o);
                else if (nk.Contains(key) || Wiki.Norm(o.ToString()).Contains(key)) mid.Add(o);
            }
            pre.AddRange(mid);
            return pre.ToArray();
        }

        static object ResolveByText(List<object> master, string text)
        {
            string key = Wiki.Norm(text);
            if (key.Length == 0) return null;
            foreach (object o in master)
            {
                if (Wiki.Norm(ItemName(o)) == key) return o;
            }
            return null;
        }

        // ---------- data loading ----------
        void StartInit()
        {
            Thread t = new Thread(new ThreadStart(delegate
            {
                string cachedHtml = null;
                DateTime cachedAt = DateTime.MinValue;
                try
                {
                    if (File.Exists(CachePath))
                    {
                        cachedHtml = File.ReadAllText(CachePath, Encoding.UTF8);
                        cachedAt = File.GetLastWriteTime(CachePath);
                    }
                }
                catch { }
                if (cachedHtml != null)
                {
                    List<Chara> chars = null;
                    try { chars = Wiki.Parse(cachedHtml); } catch { }
                    if (chars != null && chars.Count >= 10)
                    {
                        List<Chara> cc = chars;
                        DateTime at = cachedAt;
                        SafeInvoke(delegate
                        {
                            ApplyData(cc, "\u524d\u56de\u53d6\u5f97\u30c7\u30fc\u30bf\uff08" + FmtDate(at) + "\uff09\u3092\u8868\u793a\u4e2d \u30fb \u6700\u65b0\u3092\u78ba\u8a8d\u3057\u3066\u3044\u307e\u3059\u2026", false);
                        });
                    }
                }
                DoFetch(false);
            }));
            t.IsBackground = true;
            t.Start();
        }

        void RefreshData(bool manual)
        {
            if (_busy) return;
            Thread t = new Thread(new ThreadStart(delegate { DoFetch(manual); }));
            t.IsBackground = true;
            t.Start();
        }

        void DoFetch(bool manual)
        {
            if (_busy) return;
            _busy = true;
            SafeInvoke(delegate
            {
                _btnReload.Enabled = false;
                SetStatus("\u6700\u65b0\u30c7\u30fc\u30bf\u3092\u53d6\u5f97\u4e2d\u2026", false);
            });
            string err = null;
            string html = null;
            try
            {
                try { ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072; }
                catch { }
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = UA;
                    byte[] data = wc.DownloadData(Wiki.Url);
                    html = Encoding.UTF8.GetString(data);
                }
            }
            catch (Exception ex) { err = ex.Message; }

            List<Chara> chars = null;
            if (html != null && html.Length >= 5000)
            {
                try { chars = Wiki.Parse(html); } catch (Exception ex) { err = ex.Message; }
            }
            else if (err == null) err = "\u5fdc\u7b54\u304c\u4e0d\u5b8c\u5168\u3067\u3057\u305f";

            if (chars != null && chars.Count >= 10)
            {
                try { File.WriteAllText(CachePath, html, Encoding.UTF8); } catch { }
                List<Chara> cc = chars;
                SafeInvoke(delegate
                {
                    ApplyData(cc, "\u6700\u65b0\u30c7\u30fc\u30bf\u53d6\u5f97\u6e08\u307f\uff08" + FmtDate(DateTime.Now) + "\uff09 \u30fb \u53ce\u9332\u30ad\u30e3\u30e9 " + cc.Count.ToString() + "\u4eba", false);
                    _btnReload.Enabled = true;
                });
            }
            else
            {
                if (chars != null && err == null) err = "\u30da\u30fc\u30b8\u69cb\u9020\u3092\u89e3\u6790\u3067\u304d\u307e\u305b\u3093\u3067\u3057\u305f";
                string msg = err == null ? "" : err;
                SafeInvoke(delegate
                {
                    _btnReload.Enabled = true;
                    if (_chars.Count > 0)
                    {
                        SetStatus("\u30aa\u30d5\u30e9\u30a4\u30f3: \u4fdd\u5b58\u6e08\u307f\u30c7\u30fc\u30bf\u3092\u8868\u793a\u4e2d\uff08\u53d6\u5f97\u5931\u6557: " + msg + "\uff09", true);
                    }
                    else
                    {
                        SetStatus("\u30c7\u30fc\u30bf\u3092\u53d6\u5f97\u3067\u304d\u307e\u305b\u3093\u3067\u3057\u305f: " + msg + " \uff0f \u5bfe\u51e6: \u30cd\u30c3\u30c8\u63a5\u7d9a\u3092\u78ba\u8a8d\u3057\u3066\u300c\u6700\u65b0\u30c7\u30fc\u30bf\u3092\u518d\u53d6\u5f97\u300d\u3001\u307e\u305f\u306f\u300c\u4fdd\u5b58\u3057\u305fHTML\u3092\u8aad\u307f\u8fbc\u3080\u300d", true);
                        if (manual)
                        {
                            MessageBox.Show(this,
                                "Wiki\u304b\u3089\u30c7\u30fc\u30bf\u3092\u53d6\u5f97\u3067\u304d\u307e\u305b\u3093\u3067\u3057\u305f\u3002\n\u30a8\u30e9\u30fc: " + msg +
                                "\n\n\u5bfe\u51e6\u6cd5:\n1. \u30a4\u30f3\u30bf\u30fc\u30cd\u30c3\u30c8\u63a5\u7d9a\u3092\u78ba\u8a8d\u3057\u3066\u518d\u5ea6\u304a\u8a66\u3057\u304f\u3060\u3055\u3044\u3002\n2. \u30d6\u30e9\u30a6\u30b6\u3067\u300c\u30ad\u30e3\u30e9\u547c\u79f0\u8868\u300d\u3092\u958b\u304dCtrl+S\u3067\u4fdd\u5b58\u3057\u3001\u300c\u4fdd\u5b58\u3057\u305fHTML\u3092\u8aad\u307f\u8fbc\u3080\u300d\u304b\u3089\u8aad\u307f\u8fbc\u3093\u3067\u304f\u3060\u3055\u3044\u3002",
                                "\u53d6\u5f97\u30a8\u30e9\u30fc", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                });
            }
            _busy = false;
        }

        void ImportHtml()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "HTML\u30d5\u30a1\u30a4\u30eb (*.html;*.htm)|*.html;*.htm|\u3059\u3079\u3066\u306e\u30d5\u30a1\u30a4\u30eb (*.*)|*.*";
            dlg.Title = "\u4fdd\u5b58\u3057\u305f\u30ad\u30e3\u30e9\u547c\u79f0\u8868\u306eHTML\u3092\u9078\u629e";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                byte[] bytes = File.ReadAllBytes(dlg.FileName);
                string best = null;
                List<Chara> bestChars = null;
                string[] encodings = new string[] { "utf-8", "shift_jis", "euc-jp" };
                foreach (string encName in encodings)
                {
                    string text;
                    try { text = Encoding.GetEncoding(encName).GetString(bytes); }
                    catch { continue; }
                    List<Chara> chars;
                    try { chars = Wiki.Parse(text); } catch { continue; }
                    if (bestChars == null || chars.Count > bestChars.Count)
                    {
                        best = text;
                        bestChars = chars;
                    }
                    if (chars.Count >= 10) break;
                }
                if (bestChars == null || bestChars.Count < 5)
                {
                    MessageBox.Show(this,
                        "\u3053\u306e\u30d5\u30a1\u30a4\u30eb\u304b\u3089\u547c\u79f0\u30c7\u30fc\u30bf\u3092\u8aad\u307f\u53d6\u308c\u307e\u305b\u3093\u3067\u3057\u305f\u3002\n\u300c\u30ad\u30e3\u30e9\u547c\u79f0\u8868\u300d\u306e\u30da\u30fc\u30b8\u3092Ctrl+S\uff08\u5f62\u5f0f:\u300c\u30a6\u30a7\u30d6\u30da\u30fc\u30b8\u3001HTML\u306e\u307f\u300d\uff09\u3067\u4fdd\u5b58\u3057\u305f\u30d5\u30a1\u30a4\u30eb\u3092\u9078\u3093\u3067\u304f\u3060\u3055\u3044\u3002",
                        "\u8aad\u307f\u8fbc\u307f\u30a8\u30e9\u30fc", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try { File.WriteAllText(CachePath, best, Encoding.UTF8); } catch { }
                ApplyData(bestChars, "\u624b\u52d5\u8aad\u307f\u8fbc\u307f\u5b8c\u4e86\uff08" + Path.GetFileName(dlg.FileName) + "\uff09 \u30fb \u53ce\u9332\u30ad\u30e3\u30e9 " + bestChars.Count.ToString() + "\u4eba", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "\u8aad\u307f\u8fbc\u307f\u306b\u5931\u6557\u3057\u307e\u3057\u305f: " + ex.Message, "\u30a8\u30e9\u30fc", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void SafeInvoke(MethodInvoker act)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(act);
                else act();
            }
            catch { }
        }

        void SetStatus(string text, bool isError)
        {
            _status.Text = text;
            _status.ForeColor = isError ? Color.Firebrick : MutedFore;
            _tip.SetToolTip(_status, text);
        }

        static string FmtDate(DateTime dt)
        {
            return dt.ToString("yyyy/MM/dd HH:mm");
        }

        // ---------- applying data ----------
        void ApplyData(List<Chara> chars, string status, bool isError)
        {
            _chars = chars;
            _charIndex.Clear();
            foreach (Chara c in chars)
            {
                string k = Wiki.Norm(c.Name);
                if (!_charIndex.ContainsKey(k)) _charIndex[k] = c;
            }
            _charItems.Clear();
            foreach (Chara c in chars) _charItems.Add(new CharItem(c));

            Dictionary<string, OrgItem> orgMap = new Dictionary<string, OrgItem>();
            foreach (Chara c in chars)
            {
                HashSet<string> seen = new HashSet<string>();
                foreach (Entry en in c.Rows)
                {
                    string k = Wiki.Norm(en.Target);
                    if (k.Length == 0 || _charIndex.ContainsKey(k)) continue;
                    if (!seen.Add(k)) continue;
                    OrgItem oi;
                    if (!orgMap.TryGetValue(k, out oi))
                    {
                        oi = new OrgItem();
                        oi.Key = k;
                        oi.Name = en.Target;
                        orgMap[k] = oi;
                    }
                    oi.Count++;
                }
            }
            List<OrgItem> orgs = new List<OrgItem>(orgMap.Values);
            orgs.Sort(delegate(OrgItem a, OrgItem b)
            {
                int cmp = b.Count.CompareTo(a.Count);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.Name, b.Name);
            });
            _orgItems.Clear();
            foreach (OrgItem oi in orgs) _orgItems.Add(oi);

            // keep / restore selections
            _restoring = true;
            try
            {
                string nameA = _selA != null ? _selA.Name : null;
                string nameB = _selB != null ? _selB.Name : null;
                string nameO = _selO != null ? _selO.Name : null;
                if (nameA == null && nameB == null && nameO == null)
                {
                    string[] saved = LoadSel();
                    if (saved != null)
                    {
                        nameA = saved.Length > 0 ? saved[0] : null;
                        nameB = saved.Length > 1 ? saved[1] : null;
                        nameO = saved.Length > 2 ? saved[2] : null;
                    }
                }
                _selA = FindChar(nameA);
                _selB = FindChar(nameB);
                _selO = FindOrg(nameO);
                RefillCombo(_cbA, _charItems, _selA != null ? _selA.Name : "");
                RefillCombo(_cbB, _charItems, _selB != null ? _selB.Name : "");
                RefillCombo(_cbO, _orgItems, _selO != null ? _selO.Name : "");
            }
            finally { _restoring = false; }

            SetStatus(status, isError);
            RenderPair();
            RenderOrg();
        }

        Chara FindChar(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Chara c;
            return _charIndex.TryGetValue(Wiki.Norm(name), out c) ? c : null;
        }

        OrgItem FindOrg(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string k = Wiki.Norm(name);
            foreach (object o in _orgItems)
            {
                OrgItem oi = (OrgItem)o;
                if (oi.Key == k) return oi;
            }
            return null;
        }

        void RefillCombo(ComboBox cb, List<object> items, string text)
        {
            _guard = true;
            try
            {
                cb.BeginUpdate();
                cb.Items.Clear();
                cb.Items.AddRange(items.ToArray());
                cb.EndUpdate();
                cb.Text = text;
            }
            finally { _guard = false; }
        }

        void SaveSel()
        {
            if (_restoring) return;
            try
            {
                string[] lines = new string[]
                {
                    _selA != null ? _selA.Name : "",
                    _selB != null ? _selB.Name : "",
                    _selO != null ? _selO.Name : ""
                };
                File.WriteAllLines(SettingsPath, lines, Encoding.UTF8);
            }
            catch { }
        }

        string[] LoadSel()
        {
            try
            {
                if (File.Exists(SettingsPath)) return File.ReadAllLines(SettingsPath, Encoding.UTF8);
            }
            catch { }
            return null;
        }

        void SwapSelection()
        {
            Chara tmp = _selA;
            _selA = _selB;
            _selB = tmp;
            _guard = true;
            try
            {
                _cbA.Text = _selA != null ? _selA.Name : "";
                _cbB.Text = _selB != null ? _selB.Name : "";
            }
            finally { _guard = false; }
            SaveSel();
            RenderPair();
        }

        // ---------- rendering ----------
        static void ClearFlow(FlowLayoutPanel flow)
        {
            while (flow.Controls.Count > 0)
            {
                Control c = flow.Controls[0];
                flow.Controls.RemoveAt(0);
                c.Dispose();
            }
        }

        Label MakeChip(Seg s)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Padding = new Padding(9, 5, 9, 5);
            l.Margin = new Padding(4);
            l.BackColor = ChipBack;
            l.ForeColor = ChipFore;
            string txt = s.Text.Length > 0 ? s.Text : "\uff08\u8a18\u8f09\u306a\u3057\uff09";
            if (s.Marker != null) txt += " " + s.Marker;
            l.Text = txt;
            if (s.Note != null) _tip.SetToolTip(l, s.Note);
            return l;
        }

        Label MakeMuted(string text)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Margin = new Padding(6);
            l.ForeColor = MutedFore;
            l.Text = text;
            return l;
        }

        void RenderPair()
        {
            _selfA.Text = _selA != null && _selA.Self.Count > 0 ? "\u4e00\u4eba\u79f0: " + Wiki.JoinSegs(_selA.Self) : "";
            _tip.SetToolTip(_selfA, _selfA.Text);
            _selfB.Text = _selB != null && _selB.Self.Count > 0 ? "\u4e00\u4eba\u79f0: " + Wiki.JoinSegs(_selB.Self) : "";
            _tip.SetToolTip(_selfB, _selfB.Text);
            SetCharIcon(_picA, _selA);
            SetCharIcon(_picB, _selB);

            RenderDirection(_headAB, _flowAB, _selA, _selB);
            RenderDirection(_headBA, _flowBA, _selB, _selA);
        }

        void RenderDirection(Label head, FlowLayoutPanel flow, Chara from, Chara to)
        {
            ClearFlow(flow);
            if (from == null || to == null)
            {
                head.Text = "\u2015 \u2192 \u2015";
                flow.Controls.Add(MakeMuted("\u30ad\u30e3\u30e9\u30af\u30bf\u30fc\u30922\u4eba\u9078\u629e\u3057\u3066\u304f\u3060\u3055\u3044"));
                return;
            }
            head.Text = from.Name + " \u2192 " + to.Name + " \u306e\u547c\u3073\u65b9";
            if (Wiki.Norm(from.Name) == Wiki.Norm(to.Name))
            {
                if (from.Self.Count > 0)
                {
                    foreach (Seg s in from.Self) flow.Controls.Add(MakeChip(s));
                }
                else flow.Controls.Add(MakeMuted("\u4e00\u4eba\u79f0\u306e\u8a18\u8f09\u306a\u3057"));
                return;
            }
            string key = Wiki.Norm(to.Name);
            bool found = false;
            foreach (Entry en in from.Rows)
            {
                if (Wiki.Norm(en.Target) != key) continue;
                foreach (Seg s in en.Calls) flow.Controls.Add(MakeChip(s));
                found = true;
            }
            if (!found) flow.Controls.Add(MakeMuted("\u8a18\u8f09\u306a\u3057\uff08Wiki\u306b\u672a\u8a18\u8f09\u306e\u7d44\u307f\u5408\u308f\u305b\u3067\u3059\uff09"));
        }

        void RenderOrg()
        {
            ClearFlow(_orgFlow);
            if (_selO == null)
            {
                _orgFlow.Controls.Add(MakeMuted("\u7d44\u7e54\u30fb\u30b0\u30eb\u30fc\u30d7\u3092\u9078\u629e\u3059\u308b\u3068\u3001\u5168\u30ad\u30e3\u30e9\u30af\u30bf\u30fc\u304b\u3089\u306e\u547c\u79f0\u3092\u4e00\u89a7\u8868\u793a\u3057\u307e\u3059"));
                return;
            }
            int shown = 0;
            foreach (Chara c in _chars)
            {
                List<Seg> all = new List<Seg>();
                foreach (Entry en in c.Rows)
                {
                    if (Wiki.Norm(en.Target) == _selO.Key) all.AddRange(en.Calls);
                }
                if (all.Count == 0) continue;
                _orgFlow.Controls.Add(MakeOrgRow(c, all));
                shown++;
            }
            if (shown == 0) _orgFlow.Controls.Add(MakeMuted("\u8a18\u8f09\u306a\u3057"));
        }

        Control MakeOrgRow(Chara c, List<Seg> segs)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.WrapContents = true;
            row.Margin = new Padding(3, 3, 3, 3);
            row.MaximumSize = new Size(Math.Max(300, _orgFlow.ClientSize.Width - 28), 0);
            row.BackColor = Color.White;

            PictureBox pic = new PictureBox();
            pic.Size = new Size(30, 30);
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.Margin = new Padding(4, 4, 2, 4);
            row.Controls.Add(pic);
            SetCharIcon(pic, c);

            Label name = new Label();
            name.AutoSize = true;
            name.Font = new Font(Font, FontStyle.Bold);
            name.Margin = new Padding(4, 10, 0, 4);
            name.Text = c.Name;
            row.Controls.Add(name);

            string metaText = c.School;
            if (c.Group.Length > 0) metaText = metaText.Length > 0 ? metaText + " / " + c.Group : c.Group;
            if (metaText.Length > 0)
            {
                Label meta = new Label();
                meta.AutoSize = true;
                meta.ForeColor = MutedFore;
                meta.Margin = new Padding(4, 11, 6, 4);
                meta.Text = metaText;
                row.Controls.Add(meta);
            }
            foreach (Seg s in segs) row.Controls.Add(MakeChip(s));
            return row;
        }

        const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        // ---------- icons ----------
        void SetCharIcon(PictureBox pic, Chara c)
        {
            if (c == null)
            {
                pic.Image = null;
                _tip.SetToolTip(pic, "");
                return;
            }
            pic.Image = FallbackIcon(c.Name);
            List<string> urls = new List<string>();
            foreach (string s in c.Icons)
            {
                string u = Wiki.ResolveUrl(s);
                if (u != null && !urls.Contains(u)) urls.Add(u);
            }
            if (urls.Count == 0)
            {
                _tip.SetToolTip(pic, c.Name + " (\u30a2\u30a4\u30b3\u30f3\u753b\u50cf\u306a\u3057)");
                return;
            }
            _tip.SetToolTip(pic, urls[0]);
            lock (_iconCache)
            {
                foreach (string u in urls)
                {
                    Image cachedImg;
                    if (_iconCache.TryGetValue(u, out cachedImg))
                    {
                        pic.Image = cachedImg;
                        return;
                    }
                }
            }
            List<string> urlList = urls;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Image img = null;
                string okUrl = null;
                string lastErr = null;
                foreach (string u in urlList)
                {
                    string err;
                    img = TryLoadIcon(u, out err);
                    if (img != null) { okUrl = u; break; }
                    if (err != null) lastErr = err;
                }
                if (img == null)
                {
                    string msg = lastErr == null ? "" : lastErr;
                    SafeInvoke(delegate
                    {
                        if (!pic.IsDisposed) _tip.SetToolTip(pic, "\u30a2\u30a4\u30b3\u30f3\u53d6\u5f97\u5931\u6557: " + msg + " / " + urlList[0]);
                    });
                    return;
                }
                lock (_iconCache) { _iconCache[okUrl] = img; }
                Image finalImg = img;
                string finalUrl = okUrl;
                SafeInvoke(delegate
                {
                    if (!pic.IsDisposed)
                    {
                        pic.Image = finalImg;
                        _tip.SetToolTip(pic, finalUrl);
                    }
                });
            });
        }

        Image TryLoadIcon(string url, out string error)
        {
            error = null;
            string file = Path.Combine(IconDir, Md5Hex(url) + ".bin");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool fromFile = false;
                try
                {
                    byte[] bytes = null;
                    if (attempt == 0 && File.Exists(file))
                    {
                        bytes = File.ReadAllBytes(file);
                        fromFile = true;
                    }
                    else
                    {
                        using (WebClient wc = new WebClient())
                        {
                            wc.Headers[HttpRequestHeader.UserAgent] = UA;
                            wc.Headers[HttpRequestHeader.Referer] = Wiki.Url;
                            wc.Headers[HttpRequestHeader.Accept] = "image/png,image/jpeg,image/gif,image/webp,image/*;q=0.9,*/*;q=0.8";
                            bytes = wc.DownloadData(url);
                        }
                        if (bytes == null || bytes.Length < 30)
                        {
                            error = "empty response";
                            return null;
                        }
                        string head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 64)).TrimStart().ToLowerInvariant();
                        if (head.StartsWith("<") || head.StartsWith("{"))
                        {
                            error = "not an image";
                            return null;
                        }
                        try
                        {
                            Directory.CreateDirectory(IconDir);
                            File.WriteAllBytes(file, bytes);
                        }
                        catch { }
                    }
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (Image tmp = Image.FromStream(ms))
                    {
                        return new Bitmap(tmp);
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (fromFile)
                    {
                        try { File.Delete(file); } catch { }
                        continue;
                    }
                    return null;
                }
            }
            return null;
        }
        Image FallbackIcon(string name)
        {
            string key = name.Length > 0 ? name.Substring(0, 1) : "?";
            Image cached;
            if (_fallbackCache.TryGetValue(key, out cached)) return cached;
            Bitmap bmp = new Bitmap(56, 56);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int hash = 0;
                foreach (char ch in name) hash = hash * 31 + ch;
                Color col = Color.FromArgb(120 + Math.Abs(hash % 100), 140 + Math.Abs((hash / 7) % 80), 200 + Math.Abs((hash / 13) % 55));
                using (SolidBrush b = new SolidBrush(col)) g.FillEllipse(b, 2, 2, 52, 52);
                using (Font f = new Font("Yu Gothic UI", 20F, FontStyle.Bold))
                using (SolidBrush tb = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(key, f, tb, new RectangleF(0, 0, 56, 56), sf);
                    sf.Dispose();
                }
            }
            _fallbackCache[key] = bmp;
            return bmp;
        }

        static string Md5Hex(string s)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--test")
            {
                string html = File.ReadAllText(args[1], Encoding.UTF8);
                List<Chara> chars = Wiki.Parse(html);
                Console.WriteLine("CHAR_COUNT: " + chars.Count.ToString());
                foreach (Chara c in chars)
                {
                    Console.WriteLine(c.Name + " | school=" + c.School + " | group=" + c.Group + " | rows=" + c.Rows.Count.ToString() + " | self=" + Wiki.JoinSegs(c.Self));
                }
                return;
            }
            try { ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072 | (SecurityProtocolType)768; } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}

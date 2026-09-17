using System.Drawing;
namespace FloatingTasks {
 // 暗色配色令牌：石墨灰底 + 柔和浅字，适配桌面半透明显示。
 // 语义色仅两种：蓝 Accent = 进行/选中/主操作，绿 Done = 完成/成功/状态点。
 // 禁止在绘制代码中出现令牌外的硬编码颜色。
 internal static class Palette {
  public static readonly Color Bg = Color.FromArgb(27,30,35);        // 主面板底色、输入框
  public static readonly Color Panel = Color.FromArgb(23,26,31);     // 左/右栏底色、便签书写区
  public static readonly Color Hover = Color.FromArgb(42,47,55);     // 行悬停、幽灵按钮底
  public static readonly Color Border = Color.FromArgb(57,63,73);    // 面板 1px 描边
  public static readonly Color Divider = Color.FromArgb(39,44,52);   // 行间分隔线
  public static readonly Color RunBg = Color.FromArgb(30,42,56);     // 计时中行底
  public static readonly Color Accent = Color.FromArgb(126,179,238);     // 强调蓝
  public static readonly Color AccentDark = Color.FromArgb(151,195,245); // 主按钮悬停蓝
  public static readonly Color AccentSoft = Color.FromArgb(38,56,77);// 蓝软底
  public static readonly Color Done = Color.FromArgb(117,203,159);       // 完成绿
  public static readonly Color DoneSoft = Color.FromArgb(30,49,42);  // 绿软底（状态胶囊）
  public static readonly Color Ink = Color.FromArgb(230,234,240);          // 主文
  public static readonly Color Muted = Color.FromArgb(174,183,196);     // 次文、图标
  public static readonly Color Faint = Color.FromArgb(139,150,167);     // 弱提示
  public static readonly Color Danger = Color.FromArgb(240,139,132);      // 危险（仅文字/小图标）
  public static readonly Color InputBorder = Color.FromArgb(76,85,99); // 输入框描边
  public static readonly Color CircleLine = Color.FromArgb(114,126,144);  // 完成圆圈空心描边
  public static readonly Color SwitchOff = Color.FromArgb(66,75,88);   // 拨杆关闭态
  public static readonly Color OnAccent = Color.FromArgb(18,29,43); // 实心强调按钮文字
  // 旧代码引用名 → 新令牌的兼容别名，逐步收敛
  public static readonly Color Surface = Bg;
  public static readonly Color Side = Panel;
  public static readonly Color Row = Bg;
  public static readonly Color RowActive = RunBg;
  public static readonly Color Selected = AccentSoft;
  public static readonly Color Input = Bg;
  public static readonly Color Hairline = Border;
  public static readonly Color AccentLine = Color.FromArgb(120,Accent);
  public static readonly Color Track = Color.FromArgb(53,61,73);
 }
}

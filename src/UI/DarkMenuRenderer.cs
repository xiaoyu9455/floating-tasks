using System.Drawing;
using System.Windows.Forms;

namespace FloatingTasks {
 // Explicit rendering prevents the Windows light menu theme from leaking into dark menus.
 internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer {
  public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
  protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
   e.TextColor = e.Item.Enabled ? Palette.Ink : Palette.Faint;
   base.OnRenderItemText(e);
  }
  protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
   e.ArrowColor = e.Item.Enabled ? Palette.Ink : Palette.Faint;
   base.OnRenderArrow(e);
  }
  protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
   Rectangle r = e.ImageRectangle;
   using (var pen = new Pen(Palette.Accent, 2)) {
    e.Graphics.DrawLines(pen, new Point[] {
     new Point(r.Left + r.Width / 5, r.Top + r.Height / 2),
     new Point(r.Left + r.Width * 2 / 5, r.Top + r.Height * 3 / 4),
     new Point(r.Left + r.Width * 4 / 5, r.Top + r.Height / 4)
    });
   }
  }
 }
 internal sealed class DarkMenuColors : ProfessionalColorTable {
  public DarkMenuColors() { UseSystemColors = false; }
  public override Color ToolStripDropDownBackground { get { return Palette.Panel; } }
  public override Color MenuBorder { get { return Palette.Border; } }
  public override Color MenuItemBorder { get { return Palette.InputBorder; } }
  public override Color MenuItemSelected { get { return Palette.Hover; } }
  public override Color MenuItemSelectedGradientBegin { get { return Palette.Hover; } }
  public override Color MenuItemSelectedGradientEnd { get { return Palette.Hover; } }
  public override Color MenuItemPressedGradientBegin { get { return Palette.Selected; } }
  public override Color MenuItemPressedGradientMiddle { get { return Palette.Selected; } }
  public override Color MenuItemPressedGradientEnd { get { return Palette.Selected; } }
  public override Color ImageMarginGradientBegin { get { return Palette.Panel; } }
  public override Color ImageMarginGradientMiddle { get { return Palette.Panel; } }
  public override Color ImageMarginGradientEnd { get { return Palette.Panel; } }
  public override Color SeparatorDark { get { return Palette.Border; } }
  public override Color SeparatorLight { get { return Palette.Panel; } }
  public override Color CheckBackground { get { return Palette.Selected; } }
  public override Color CheckSelectedBackground { get { return Palette.Selected; } }
  public override Color CheckPressedBackground { get { return Palette.Selected; } }
 }
}
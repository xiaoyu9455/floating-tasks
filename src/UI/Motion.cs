using System;
using System.Collections.Generic;
using System.Windows.Forms;
namespace FloatingTasks {
 // 极简动画时钟：注册式数值插值，16ms 帧驱动，统一 ease-out cubic（禁止线性动画）。
 // 绘制时调用 Value(key, target, durationMs) 读取当前插值；目标值变化时自动从当前值过渡。
 // 仅服务视觉层（悬停淡入淡出、运行/暂停颜色过渡等），不影响布局、热区与数据。
 internal sealed class Motion : IDisposable {
  class Track { public double From, To; public long Start; public int Duration; }
  readonly Dictionary<string,Track> tracks = new Dictionary<string,Track>();
  readonly Control owner;
  readonly Timer timer;
  bool animating;
  public Motion(Control owner) {
   this.owner = owner;
   timer = new Timer { Interval = 16 };
   timer.Tick += Tick;
  }
  public double Value(string key, double target, int duration) {
   Track t;
   if(!tracks.TryGetValue(key,out t)) {
    // 首次出现直接落在目标值，避免界面初次绘制时集体"闪入"。
    t = new Track { From = target, To = target, Start = Environment.TickCount, Duration = duration };
    tracks[key] = t;
   }
   if(Math.Abs(t.To - target) > 0.0001) {
    t.From = Current(t); t.To = target; t.Start = Environment.TickCount; t.Duration = duration;
   }
   double v = Current(t);
   if(v != t.To) StartClock();
   return v;
  }
  static double Current(Track t) {
   double p = t.Duration <= 0 ? 1 : Math.Min(1,(double)(Environment.TickCount - t.Start) / t.Duration);
   if(p >= 1) return t.To;
   double e = 1 - Math.Pow(1 - p,3); // ease-out cubic，近似 cubic-bezier(0.22,1,0.36,1)
   return t.From + (t.To - t.From) * e;
  }
  void StartClock() { if(!animating) { animating = true; timer.Start(); } }
  void Tick(object sender, EventArgs e) {
   bool active = false;
   foreach(Track t in tracks.Values) if(Current(t) != t.To) { active = true; break; }
   if(!active) { animating = false; timer.Stop(); return; }
   owner.Invalidate();
  }
  public void Dispose() { timer.Stop(); timer.Dispose(); }
 }
}

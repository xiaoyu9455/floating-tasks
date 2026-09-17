using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace FloatingTasks {
 public partial class MainWindow {
  bool notesOpen, refreshingNotes;
  Panel notesPanel;
  TextBox noteInput;
  ListBox noteList;
  Label noteStatus;
  Button noteSave, noteNew, noteDelete, noteUndo;
  string editingNoteId;
  int NotesBodyHeight { get {
   int mainHeight=MainListHeight;
   int available=(int)(Screen.FromControl(this).WorkingArea.Height/scale)-mainHeight-48;
   return Math.Max(100,Math.Min(260,available));
  } }
  int NotesHeight { get { return 32 + (notesOpen ? NotesBodyHeight : 0); } }
  Button NoteButton(string text, Action action) { return NoteButton(text,action,"ghost"); }
  Button NoteButton(string text, Action action, string style) {
   var button = new Button { Text=text, FlatStyle=FlatStyle.Flat, TabStop=true };
   if(style=="primary") {
    button.BackColor=Palette.Accent; button.ForeColor=Palette.OnAccent;
    button.FlatAppearance.BorderSize=0; button.FlatAppearance.MouseOverBackColor=Palette.AccentDark;
   } else {
    // 幽灵按钮：暗底 + 1px 描边；删除仅用危险红文字，底色不红
    button.BackColor=Palette.Bg; button.ForeColor=style=="danger"?Palette.Danger:Palette.Muted;
    button.FlatAppearance.BorderColor=Palette.InputBorder; button.FlatAppearance.MouseOverBackColor=Palette.Hover;
   }
   button.Click += delegate { action(); }; return button;
  }
  void InitializeNotes() {
   notesPanel = new Panel { BackColor=surface, AutoScroll=true };
   noteInput = new TextBox { Multiline=true, AcceptsReturn=true, ScrollBars=ScrollBars.Vertical,
    MaxLength=20000, BackColor=Palette.Panel, ForeColor=ink, BorderStyle=BorderStyle.FixedSingle, AccessibleName="便签内容" };
   noteList = new ListBox { BackColor=Palette.Bg, ForeColor=ink, BorderStyle=BorderStyle.None,
    IntegralHeight=false, HorizontalScrollbar=true, AccessibleName="已保存便签" };
   UseDarkControlTheme(noteInput); UseDarkControlTheme(noteList); UseDarkControlTheme(notesPanel);
   noteStatus = new Label { ForeColor=Palette.Faint, Text="输入便签 · Ctrl+Enter 保存", AutoEllipsis=true };
   noteSave=NoteButton("保存",SaveNoteInput,"primary");
   noteNew=NoteButton("新建",delegate {
    if(noteInput.Modified && !String.IsNullOrWhiteSpace(noteInput.Text)) SaveNoteInput();
    if(noteInput.Modified && !String.IsNullOrWhiteSpace(noteInput.Text)) return;
    editingNoteId=null; noteInput.Clear(); noteInput.Modified=false; noteList.ClearSelected(); noteInput.Focus();
   });
   noteDelete=NoteButton("删除",delegate {
    if(editingNoteId==null)return;
    session.DeleteNote(editingNoteId); editingNoteId=null; noteInput.Clear(); noteInput.Modified=false;
    RefreshNotes(); UpdateLayout(true);
   },"danger");
   noteUndo=NoteButton("撤销删除",delegate { session.UndoNoteDelete(); RefreshNotes(); UpdateLayout(true); });
   noteInput.KeyDown += delegate(object sender,KeyEventArgs e) {
    if(e.Control && e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; SaveNoteInput(); }
   };
   noteList.SelectedIndexChanged += delegate {
    if(refreshingNotes)return;
    var note=noteList.SelectedItem as Note;
    if(note==null)return;
    if(noteInput.Modified && !String.IsNullOrWhiteSpace(noteInput.Text)) {
     SaveNoteInput();
     if(noteInput.Modified)return;
    }
    editingNoteId=note.Id; noteInput.Text=note.Content; noteInput.Modified=false; RefreshNotes();
    noteStatus.Text=(note.Source=="agent"?"模型记录":"手动记录")+" · "+note.Created.ToString("MM-dd HH:mm");
   };
   notesPanel.Controls.AddRange(new Control[]{noteInput,noteList,noteStatus,noteSave,noteNew,noteDelete,noteUndo});
   Controls.Add(notesPanel); RefreshNotes();
  }
  void SaveNoteInput() {
   if(String.IsNullOrWhiteSpace(noteInput.Text)) { noteInput.Focus(); return; }
   try {
    Note note=session.SaveNote(noteInput.Text,editingNoteId);
    // Keep the ID even on disk failure so a retry updates instead of duplicating.
    editingNoteId=note.Id;
    if(session.Flush()) { noteInput.Modified=false; noteStatus.Text="已保存 · Ctrl+Enter 保存"; }
    else { noteInput.Modified=true; noteStatus.Text=notice; }
    RefreshNotes(); Invalidate();
   } catch(ArgumentException ex) { noteStatus.Text=ex.Message; }
  }
  void RefreshNotes() {
   if(noteList==null)return;
   refreshingNotes=true;
   try {
    noteList.BeginUpdate(); noteList.Items.Clear();
    foreach(Note note in db.Notes)noteList.Items.Add(note);
    noteList.SelectedItem=db.Notes.FirstOrDefault(n=>n.Id==editingNoteId);
    noteUndo.Enabled=session.CanUndoNote; if(notice!=null)noteStatus.Text=notice;
   } finally { noteList.EndUpdate(); refreshingNotes=false; }
  }
  void LayoutNotes(int y) {
   notesPanel.SetBounds(S(centerX+12),S(y+32),S(CenterWidth-24),S(NotesBodyHeight-8));
   string fontKey="notes"+S(11);
   Font noteFont;
   if(!fonts.TryGetValue(fontKey,out noteFont)) {
    noteFont=new Font("Microsoft YaHei UI",S(11),FontStyle.Regular,GraphicsUnit.Pixel); fonts.Add(fontKey,noteFont);
   }
   notesPanel.Font=noteFont;
   notesPanel.AutoScrollMinSize=new Size(0,S(245));
   noteInput.SetBounds(0,0,S(CenterWidth-44),S(78));
   noteSave.SetBounds(0,S(84),S(52),S(28));
   noteNew.SetBounds(S(56),S(84),S(52),S(28));
   noteDelete.SetBounds(S(112),S(84),S(52),S(28));
   noteUndo.SetBounds(S(168),S(84),S(60),S(28));
   noteStatus.SetBounds(0,S(116),S(CenterWidth-44),S(20));
   noteList.SetBounds(0,S(140),S(CenterWidth-44),S(105));
  }
  void PaintNotes(Graphics g,int y) {
   using(Pen divider=new Pen(Palette.Border,1)) g.DrawLine(divider,centerX+16,y-4,centerX+CenterWidth-16,y-4);
   TextAt(g,"便签",new RectangleF(centerX+18,y,60,30),ink,11.5f,true);
   TextAt(g,db.Notes.Count+" 条",new RectangleF(centerX+46,y,120,30),Palette.Faint,10.5f,false);
   Target("notesHeader",new RectangleF(centerX+12,y,CenterWidth-24,30),"展开 / 收起便签",ToggleNotes);
   DrawIcon(g,"notesToggle",notesOpen?"collapse":"expand",centerX+CenterWidth-38,y+2,"展开 / 收起便签",ToggleNotes,muted);
  }
  void ToggleNotes() { notesOpen=!notesOpen; UpdateLayout(true); if(notesOpen)noteInput.Focus(); }
 }
}

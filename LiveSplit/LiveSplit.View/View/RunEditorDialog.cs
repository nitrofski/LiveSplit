﻿using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;
using LiveSplit.Web;
using LiveSplit.Web.Share;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.View
{
    public partial class RunEditorDialog : Form
    {
        private const int ICONINDEX = 0;
        private const int SEGMENTNAMEINDEX = 1;
        private const int SPLITTIMEINDEX = 2;
        private const int SEGMENTTIMEINDEX = 3;
        private const int BESTSEGMENTINDEX = 4;
        private const int CUSTOMCOMPARISONSINDEX = 5;

        public IRun Run { get; set; }
        public LiveSplitState CurrentState { get; set; }
        protected BindingList<ISegment> SegmentList { get; set; }
        protected ITimeFormatter TimeFormatter { get; set; }
        protected IList<TimeSpan?> SegmentTimeList { get; set; }
        protected bool IsInitialized = false;

        protected TimingMethod SelectedMethod { get { return tabControl1.SelectedTab.Text == "Real Time" ? TimingMethod.RealTime : TimingMethod.GameTime; } }

        public int CurrentSplitIndexOffset { get; set; }

        public bool AllowChangingSegments { get; set; }

        public event EventHandler RunEdited;
        public event EventHandler ComparisonRenamed;
        public event EventHandler SegmentRemovedOrAdded;
        public IList<ISegment> ChangedSegments { get; set; }

        private Control eCtl;

        public Image GameIcon { get { return Run.GameIcon ?? Properties.Resources.DefaultGameIcon; } set { Run.GameIcon = value; } }
        public String GameName { get { return Run.GameName; } set { Run.GameName = value; RefreshCategoryAutoCompleteList(); } }
        public String CategoryName { get { return Run.CategoryName; } set { Run.CategoryName = value; } }
        public String Offset
        {
            get
            {
                return TimeFormatter.Format(Run.Offset);
            }
            set
            {
                if (Regex.IsMatch(value, "[^0-9:.,-]"))
                    return;

                try { Run.Offset = TimeSpanParser.Parse(value); }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }
        }
        public int AttemptCount { get { return Run.AttemptCount; } set { Run.AttemptCount = Math.Max(0, value); } }

        public RunEditorDialog(LiveSplitState state)
        {
            InitializeComponent();
            CurrentState = state;
            Run = state.Run;
            CurrentSplitIndexOffset = 0;
            AllowChangingSegments = false;
            SegmentTimeList = new List<TimeSpan?>();
            TimeFormatter = new ShortTimeFormatter();
            ChangedSegments = new List<ISegment>();
            SegmentList = new BindingList<ISegment>(Run);
            SegmentList.AllowNew = true;
            SegmentList.AllowRemove = true;
            SegmentList.AllowEdit = true;
            SegmentList.AddingNew += SegmentList_AddingNew;
            SegmentList.ListChanged += SegmentList_ListChanged;
            runGrid.AutoGenerateColumns = false;
            runGrid.AutoSize = true;
            runGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            runGrid.DataSource = SegmentList;
            runGrid.CellValueChanged += runGrid_CellValueChanged;
            runGrid.CellDoubleClick += runGrid_CellDoubleClick;
            runGrid.CellFormatting += runGrid_CellFormatting;
            runGrid.CellParsing += runGrid_CellParsing;
            runGrid.CellValidating += runGrid_CellValidating;
            runGrid.CellEndEdit += runGrid_CellEndEdit;
            runGrid.UserDeletingRow += runGrid_UserDeletingRow;
            runGrid.UserDeletedRow += runGrid_UserDeletedRow;
            runGrid.SelectionChanged += runGrid_SelectionChanged;

            var iconColumn = new DataGridViewImageColumn() { ImageLayout = DataGridViewImageCellLayout.Zoom };
            iconColumn.DataPropertyName = "Icon";
            iconColumn.Name = "Icon";
            iconColumn.Width = 50;
            iconColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            iconColumn.DefaultCellStyle.NullValue = Properties.Resources.DefaultSplitIcon;
            runGrid.Columns.Add(iconColumn);

            var column = new DataGridViewTextBoxColumn();
            column.DataPropertyName = "Name";
            column.Name = "Segment Name";
            column.MinimumWidth = 120;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            runGrid.Columns.Add(column);

            column = new DataGridViewTextBoxColumn();
            //column.DataPropertyName = "PersonalBestSplitTime";
            column.Name = "Split Time";
            column.Width = 100;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            runGrid.Columns.Add(column);

            column = new DataGridViewTextBoxColumn();
            column.Name = "Segment Time";
            column.Width = 100;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            //column.ReadOnly = true;
            runGrid.Columns.Add(column);

            column = new DataGridViewTextBoxColumn();
            //column.DataPropertyName = "BestSegmentTime";
            column.Name = "Best Segment";
            column.Width = 100;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            runGrid.Columns.Add(column);

            runGrid.EditingControlShowing += runGrid_EditingControlShowing;

            cbxGameName.DataBindings.Add("Text", this, "GameName");
            cbxRunCategory.DataBindings.Add("Text", this, "CategoryName");
            tbxTimeOffset.DataBindings.Add("Text", this, "Offset");
            tbxAttempts.DataBindings.Add("Text", this, "AttemptCount");
            picGameIcon.DataBindings.Add("Image", this, "GameIcon");

            cbxGameName.AutoCompleteSource = AutoCompleteSource.ListItems;

            Task.Factory.StartNew(() =>
                {
                    try
                    {
                        var gameNames = CompositeGameList.Instance.GetGameNames().ToArray();
                        Action invokation = () =>
                        {
                            try
                            {
                                cbxGameName.Items.AddRange(gameNames);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex);
                            }
                        };
                        if (this.InvokeRequired)
                            this.Invoke(invokation);
                        else
                            invokation();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                    }
                });

            cbxGameName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            cbxGameName.TextChanged += cbxGameName_TextChanged;

            cbxRunCategory.AutoCompleteSource = AutoCompleteSource.ListItems;
            cbxRunCategory.Items.AddRange(new String[] { "Any%", "Low%", "100%" });
            cbxRunCategory.AutoCompleteMode = AutoCompleteMode.SuggestAppend;

            RefreshCategoryAutoCompleteList();
            UpdateSegmentList();
            cbxGameName_TextChanged(this, null);
        }

        void cbxGameName_TextChanged(object sender, EventArgs e)
        {
            DeactivateAutoSplitter();
            var splitter = AutoSplitterFactory.Instance.Create(cbxGameName.Text);
            CurrentState.Run.AutoSplitter = splitter;
            if (splitter != null && CurrentState.Settings.ActiveAutoSplitters.Contains(cbxGameName.Text))
            {
                splitter.Activate(CurrentState);
                    if (CurrentState.Run.AutoSplitterSettings != null
                    && splitter.IsActivated
                    && CurrentState.Run.AutoSplitterSettings.Attributes["gameName"].InnerText == cbxGameName.Text)
                    CurrentState.Run.AutoSplitter.Component.SetSettings(CurrentState.Run.AutoSplitterSettings);
            }
            RefreshAutoSplittingUI();
        }

        private void DeactivateAutoSplitter()
        {
            if (CurrentState.Run.AutoSplitter != null)
                CurrentState.Run.AutoSplitter.Deactivate();
        }

        void RefreshCategoryAutoCompleteList()
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    String[] categoryNames;
                    try
                    {
                        categoryNames = PBTracker.Instance.GetGameCategories(PBTracker.Instance.GetGameIdByName(Run.GameName)).Select(x => x.Value).ToArray();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);

                        categoryNames = new String[] { "Any%", "Low%", "100%" };
                    }
                    Action invokation = () =>
                    {
                        try
                        {
                            cbxRunCategory.Items.Clear();
                            cbxRunCategory.Items.AddRange(categoryNames);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex);
                        }
                    };
                    if (this.InvokeRequired)
                        this.Invoke(invokation);
                    else
                        invokation();
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            });
        }

        void runGrid_SelectionChanged(object sender, EventArgs e)
        {
            UpdateButtonsStatus();
        }

        void SegmentList_ListChanged(object sender, ListChangedEventArgs e)
        {
            RaiseRunEdited();
        }

        void runGrid_UserDeletedRow(object sender, DataGridViewRowEventArgs e)
        {
            UpdateSegmentList();
        }

        private void UpdateButtonsStatus()
        {
            if (!AllowChangingSegments)
            {
                btnAdd.Enabled = false;
                btnRemove.Enabled = false;
                btnInsert.Enabled = false;
                btnMoveDown.Enabled = false;
                btnMoveUp.Enabled = false;
            }
            else
            {
                btnRemove.Enabled = SegmentList.Count > 1;
                var selectedCell = runGrid.SelectedCells.Cast<DataGridViewCell>().ToList().FirstOrDefault();
                if (selectedCell != null)
                {
                    btnMoveUp.Enabled = selectedCell.RowIndex > 0;
                    btnMoveDown.Enabled = selectedCell.RowIndex < SegmentList.Count - 1;
                }
                else
                {
                    btnMoveUp.Enabled = false;
                    btnMoveDown.Enabled = false;
                }
            }
        }

        void runGrid_UserDeletingRow(object sender, DataGridViewRowCancelEventArgs e)
        {
            var curIndex = e.Row.Index;
            var curSegment = Run[curIndex].BestSegmentTime;
            if (curIndex < Run.Count - 1)
            {
                var nextSegment = Run[curIndex + 1].BestSegmentTime;
                var newBestSegment = new Time(Run[curIndex + 1].BestSegmentTime);
                if (curSegment.RealTime != null && nextSegment.GameTime != null)
                {
                    newBestSegment.RealTime = curSegment.RealTime+nextSegment.RealTime;
                }
                if (curSegment.GameTime != null && nextSegment.GameTime != null)
                {
                    newBestSegment.GameTime = curSegment.GameTime + newBestSegment.GameTime;
                }
                Run[curIndex + 1].BestSegmentTime = newBestSegment;
            }
        }

        void runGrid_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            this.eCtl = e.Control;
            this.eCtl.TextChanged -= new EventHandler(this.eCtl_TextChanged);
            this.eCtl.KeyPress -= new KeyPressEventHandler(this.eCtl_KeyPress);
            this.eCtl.TextChanged += new EventHandler(this.eCtl_TextChanged);
            this.eCtl.KeyPress += new KeyPressEventHandler(this.eCtl_KeyPress);
        }

        private void eCtl_TextChanged(object sender, EventArgs e)
        {
            if (runGrid.CurrentCell.ColumnIndex == SPLITTIMEINDEX || runGrid.CurrentCell.ColumnIndex == BESTSEGMENTINDEX || runGrid.CurrentCell.ColumnIndex == SEGMENTTIMEINDEX || runGrid.CurrentCell.ColumnIndex >= CUSTOMCOMPARISONSINDEX)
            {
                if (Regex.IsMatch(this.eCtl.Text, "[^0-9:.,]"))
                {
                    this.eCtl.Text = Regex.Replace(this.eCtl.Text, "[^0-9:.,]", "");
                }
            }
        }

        private void eCtl_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (runGrid.CurrentCell.ColumnIndex == SPLITTIMEINDEX || runGrid.CurrentCell.ColumnIndex == BESTSEGMENTINDEX || runGrid.CurrentCell.ColumnIndex == SEGMENTTIMEINDEX || runGrid.CurrentCell.ColumnIndex >= CUSTOMCOMPARISONSINDEX)
            {
                if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar) && e.KeyChar != ':' && e.KeyChar != '.' && e.KeyChar != ',')
                {
                    e.Handled = true;
                }
            }
        }

        void runGrid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            runGrid.Rows[e.RowIndex].ErrorText = "";
        }

        void runGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex == SPLITTIMEINDEX || e.ColumnIndex == BESTSEGMENTINDEX || e.ColumnIndex == SEGMENTTIMEINDEX || e.ColumnIndex >= CUSTOMCOMPARISONSINDEX)
            {
                if (String.IsNullOrWhiteSpace(e.FormattedValue.ToString()))
                    return;

                try
                {
                    TimeSpanParser.Parse(e.FormattedValue.ToString());
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                
                    e.Cancel = true;
                    runGrid.Rows[e.RowIndex].ErrorText = "Invalid Time";
                }
            }
        }

        void runGrid_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            if (e.ColumnIndex == SPLITTIMEINDEX || e.ColumnIndex == BESTSEGMENTINDEX || e.ColumnIndex == SEGMENTTIMEINDEX || e.ColumnIndex >= CUSTOMCOMPARISONSINDEX)
            {
                if (String.IsNullOrWhiteSpace(e.Value.ToString()))
                {
                    e.Value = null;
                    if (e.ColumnIndex == SPLITTIMEINDEX)
                    {
                        var time = new Time(Run[e.RowIndex].PersonalBestSplitTime);
                        time[SelectedMethod] = null;
                        Run[e.RowIndex].PersonalBestSplitTime = time;
                    }
                    if (e.ColumnIndex == BESTSEGMENTINDEX)
                    {
                        var time = new Time(Run[e.RowIndex].BestSegmentTime);
                        time[SelectedMethod] = null;
                        Run[e.RowIndex].BestSegmentTime = time;
                    }
                    if (e.ColumnIndex == SEGMENTTIMEINDEX)
                    {
                        SegmentTimeList[e.RowIndex] = null;
                        FixSplitsFromSegments();
                        Fix();
                    }
                    if (e.ColumnIndex >= CUSTOMCOMPARISONSINDEX)
                    {
                        var time = new Time(Run[e.RowIndex].Comparisons[runGrid.Columns[e.ColumnIndex].Name]);
                        time[SelectedMethod] = null;
                        Run[e.RowIndex].Comparisons[runGrid.Columns[e.ColumnIndex].Name] = time;
                        Fix();
                    }
                    e.ParsingApplied = true;
                    return;
                }

                try
                {
                    e.Value = TimeSpanParser.Parse(e.Value.ToString());
                    if (e.ColumnIndex == SEGMENTTIMEINDEX)
                        SegmentTimeList[e.RowIndex] = (TimeSpan)e.Value;
                    if (e.ColumnIndex >= CUSTOMCOMPARISONSINDEX)
                    {
                        var time = new Time(Run[e.RowIndex].Comparisons[runGrid.Columns[e.ColumnIndex].Name]);
                        time[SelectedMethod] = (TimeSpan)e.Value;
                        Run[e.RowIndex].Comparisons[runGrid.Columns[e.ColumnIndex].Name] = time;
                    }
                    if (e.ColumnIndex == SPLITTIMEINDEX)
                    {
                        var time = new Time(Run[e.RowIndex].PersonalBestSplitTime);
                        time[SelectedMethod] = (TimeSpan)e.Value;
                        Run[e.RowIndex].PersonalBestSplitTime = time;
                    }
                    if (e.ColumnIndex == BESTSEGMENTINDEX)
                    {
                        var time = new Time(Run[e.RowIndex].BestSegmentTime);
                        time[SelectedMethod] = (TimeSpan)e.Value;
                        Run[e.RowIndex].BestSegmentTime = time;
                    }
                    e.ParsingApplied = true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                
                    e.ParsingApplied = false;
                }
            }
        }

        void runGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex == SPLITTIMEINDEX)
            {
                if (e.RowIndex < Run.Count)
                {
                    var comparisonValue = Run[e.RowIndex].PersonalBestSplitTime[SelectedMethod];
                    if (comparisonValue == null)
                    {
                        e.Value = "";
                        e.FormattingApplied = false;
                    }
                    else
                    {
                        e.Value = TimeFormatter.Format(comparisonValue);
                        e.FormattingApplied = true;
                    }
                }
            }
            else if (e.ColumnIndex == BESTSEGMENTINDEX)
            {
                if (e.RowIndex < Run.Count)
                {
                    var comparisonValue = Run[e.RowIndex].BestSegmentTime[SelectedMethod];
                    if (comparisonValue == null)
                    {
                        e.Value = "";
                        e.FormattingApplied = false;
                    }
                    else
                    {
                        e.Value = TimeFormatter.Format(comparisonValue);
                        e.FormattingApplied = true;
                    }
                }
            }
            else if (e.ColumnIndex >= CUSTOMCOMPARISONSINDEX)
            {
                if (e.RowIndex < Run.Count)
                {
                    var comparisonValue = Run[e.RowIndex].Comparisons[runGrid.Columns[e.ColumnIndex].Name][SelectedMethod];
                    if (comparisonValue == null)
                    {
                        e.Value = "";
                        e.FormattingApplied = false;
                    }
                    else
                    {
                        e.Value = TimeFormatter.Format(comparisonValue);
                        e.FormattingApplied = true;
                    }
                }
            }
            else if (e.ColumnIndex == SEGMENTTIMEINDEX)
            {
                if (e.RowIndex < SegmentTimeList.Count)
                {
                    if (SegmentTimeList[e.RowIndex] == null)
                    {
                        e.Value = "";
                        e.FormattingApplied = false;
                    }
                    else
                    {
                        e.Value = TimeFormatter.Format(SegmentTimeList[e.RowIndex].Value);
                        e.FormattingApplied = true;
                    }
                }
            }
            else if (e.ColumnIndex == ICONINDEX)
            {
                if (e.RowIndex == Run.Count)
                {
                    e.Value = Properties.Resources.DefaultSplitIcon;
                    e.FormattingApplied = true;
                }
            }
        }

        void runGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 0 && e.RowIndex >= 0 && e.RowIndex < Run.Count)
            {
                var dialog = new OpenFileDialog();
                dialog.Filter = "Image Files|*.BMP;*.JPG;*.GIF;*.JPEG;*.PNG|All files (*.*)|*.*";
                if (!String.IsNullOrEmpty(Run[e.RowIndex].Name))
                {
                    dialog.Title = "Set Icon for " + Run[e.RowIndex].Name + "...";
                }
                else
                {
                    dialog.Title = "Set Icon...";
                }
                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        runGrid.Rows[e.RowIndex].Cells[ICONINDEX].Value = Image.FromFile(dialog.FileName);
                        runGrid.NotifyCurrentCellDirty(true);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);

                        MessageBox.Show("Could not load image!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void Fix()
        {
            Run.FixSplits();
            UpdateSegmentList();
            runGrid.InvalidateColumn(SPLITTIMEINDEX);
            runGrid.InvalidateColumn(BESTSEGMENTINDEX);
            runGrid.InvalidateColumn(SEGMENTTIMEINDEX);
            for (var index = CUSTOMCOMPARISONSINDEX; index < runGrid.Columns.Count; index++)
                runGrid.InvalidateColumn(index);
        }

        void runGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            RaiseRunEdited();
            if (e.ColumnIndex == SEGMENTTIMEINDEX)
                FixSplitsFromSegments();
            Fix();

            if (!ChangedSegments.Contains(Run[e.RowIndex]))
                ChangedSegments.Add(Run[e.RowIndex]);
        }

        void SegmentList_AddingNew(object sender, AddingNewEventArgs e)
        {
            e.NewObject = new Segment("");
        }

        private void picGameIcon_DoubleClick(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog();
            if (!String.IsNullOrEmpty(GameName))
            {
                dialog.Title = "Set Icon for " + GameName + "...";
            }
            else
            {
                dialog.Title = "Set Game Icon...";
            }
            dialog.Filter = "Image Files|*.BMP;*.JPG;*.GIF;*.JPEG;*.PNG|All files (*.*)|*.*";
            var result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    GameIcon = Image.FromFile(dialog.FileName);
                    picGameIcon.Image = GameIcon;
                    RaiseRunEdited();
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                
                    MessageBox.Show("Could not load image!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {

        }

        private void UpdateSegmentList()
        {
            var previousTime = TimeSpan.Zero;
            SegmentTimeList.Clear();
            foreach (var curSeg in Run)
            {
                if (curSeg == null)
                    SegmentTimeList.Add(null);
                else
                {
                    if (curSeg.PersonalBestSplitTime[SelectedMethod] == null)
                        SegmentTimeList.Add(null);
                    else
                    {
                        SegmentTimeList.Add(curSeg.PersonalBestSplitTime[SelectedMethod] - previousTime);
                        previousTime = curSeg.PersonalBestSplitTime[SelectedMethod].Value;
                    }
                }
            }
        }

        private void FixSplitsFromSegments()
        {
            var previousTime = TimeSpan.Zero;
            var index = 0;
            var decrement = TimeSpan.Zero;
            foreach (var curSeg in Run)
            {
                if (curSeg != null)
                {
                    if (SegmentTimeList[index] != null)
                    {
                        if (curSeg.PersonalBestSplitTime[SelectedMethod] == null && index < SegmentTimeList.Count - 1)
                            decrement = SegmentTimeList[index].Value;
                        else
                        {
                            SegmentTimeList[index] -= decrement;
                            decrement = TimeSpan.Zero;
                        }
                        var time = new Time(curSeg.PersonalBestSplitTime);
                        time[SelectedMethod] = previousTime + SegmentTimeList[index].Value;
                        curSeg.PersonalBestSplitTime = time;
                        previousTime = curSeg.PersonalBestSplitTime[SelectedMethod].Value;
                    }
                    else
                    {
                        if (curSeg.PersonalBestSplitTime[SelectedMethod] != null)
                            previousTime = curSeg.PersonalBestSplitTime[SelectedMethod].Value;
                        var time = new Time(curSeg.PersonalBestSplitTime);
                        time[SelectedMethod] = null;
                        curSeg.PersonalBestSplitTime = time;
                    }
                }
                index++;
            }
        }

        private void btnInsert_Click(object sender, EventArgs e)
        {
            var newSegment = new Segment("");
            Run.ImportBestSegment(runGrid.CurrentRow.Index);
            for (var x = Run.GetMinSegmentHistoryIndex() + 1; x <= Run.RunHistory.Count; x++)
                newSegment.SegmentHistory.Add(new IndexedTime(default(Time), x));
            SegmentList.Insert(runGrid.CurrentRow.Index, newSegment);  
            //TODO: Auto Delete?
            //SegmentList[runGrid.CurrentRow.Index].BestSegmentTime = null;
            runGrid.ClearSelection();
            runGrid.CurrentCell = runGrid.Rows[runGrid.CurrentRow.Index - 1].Cells[runGrid.CurrentCell.ColumnIndex];
            runGrid.CurrentCell.Selected = true;

            Fix();

            SegmentRemovedOrAdded(null, null);
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            var newSegment = new Segment("");
            if (runGrid.CurrentRow.Index + 1 < Run.Count)
                Run.ImportBestSegment(runGrid.CurrentRow.Index + 1);
            for (var x = Run.GetMinSegmentHistoryIndex() + 1; x <= Run.RunHistory.Count; x++)
                newSegment.SegmentHistory.Add(new IndexedTime(default(Time), x));
            SegmentList.Insert(runGrid.CurrentRow.Index + 1, newSegment);
            runGrid.ClearSelection();
            runGrid.CurrentCell = runGrid.Rows[runGrid.CurrentRow.Index + 1].Cells[runGrid.CurrentCell.ColumnIndex];
            runGrid.CurrentCell.Selected = true;

            Fix();

            SegmentRemovedOrAdded(null, null);
        }

        private void runGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                foreach (var selectedObject in runGrid.SelectedCells.OfType<DataGridViewCell>().Reverse())
                {
                    var selectedCell = (DataGridViewCell)selectedObject;

                    if (Run.Count <= 1 || selectedCell.RowIndex >= Run.Count || selectedCell.RowIndex < 0)
                        continue;

                    if (selectedCell.ColumnIndex == SEGMENTNAMEINDEX)
                    {
                        selectedCell.Value = "";
                    }
                    else if (selectedCell.ColumnIndex == ICONINDEX)
                    {
                        Run[selectedCell.RowIndex].Icon = null;
                    }
                    else if (selectedCell.ColumnIndex == SPLITTIMEINDEX)
                    {
                        var time = new Time(Run[selectedCell.RowIndex].PersonalBestSplitTime);
                        time[SelectedMethod] = null;
                        Run[selectedCell.RowIndex].PersonalBestSplitTime = time;
                    }
                    else if (selectedCell.ColumnIndex == SEGMENTTIMEINDEX)
                    {
                        SegmentTimeList[selectedCell.RowIndex] = null;
                        FixSplitsFromSegments();
                    }
                    else if (selectedCell.ColumnIndex == BESTSEGMENTINDEX)
                    {
                        var time = new Time(Run[selectedCell.RowIndex].BestSegmentTime);
                        time[SelectedMethod] = null;
                        Run[selectedCell.RowIndex].BestSegmentTime = time;
                    }
                    else if (selectedCell.ColumnIndex >= CUSTOMCOMPARISONSINDEX)
                    {
                        var time = new Time(Run[selectedCell.RowIndex].Comparisons[selectedCell.OwningColumn.Name]);
                        time [SelectedMethod] = null;
                        Run[selectedCell.RowIndex].Comparisons[selectedCell.OwningColumn.Name] = time;
                    }
                    Fix();
                }
                runGrid.Invalidate();
                RaiseRunEdited();
            }
        }

        private void RemoveSelected()
        {
            foreach (var selectedObject in runGrid.SelectedCells)
            {
                var selectedCell = (DataGridViewCell)selectedObject;

                if (Run.Count <= 1 || selectedCell.RowIndex >= Run.Count || selectedCell.RowIndex < 0)
                    continue;
                FixAfterDeletion(selectedCell.RowIndex);
                if (selectedCell.RowIndex == Run.Count - 1)
                {
                    runGrid.ClearSelection();
                    runGrid.CurrentCell = runGrid.Rows[runGrid.CurrentRow.Index - 1].Cells[runGrid.CurrentCell.ColumnIndex];
                    runGrid.CurrentCell.Selected = true;
                }
                SegmentList.RemoveAt(selectedCell.RowIndex);
            }

            Fix();
            runGrid.Invalidate();

            SegmentRemovedOrAdded(null, null);
        }

        private void SwitchSegments(int segIndex)
        {
            var firstSegment = SegmentList.ElementAt(segIndex);
            var secondSegment = SegmentList.ElementAt(segIndex+1);

            firstSegment.SegmentHistory.Clear();
            secondSegment.SegmentHistory.Clear();

            for (var runIndex = Run.GetMinSegmentHistoryIndex(); runIndex <= Run.RunHistory.Count; runIndex++)
            {
                var firstHistory = firstSegment.SegmentHistory.Where(x => x.Index == runIndex).FirstOrDefault();
                var secondHistory = secondSegment.SegmentHistory.Where(x => x.Index == runIndex).FirstOrDefault();
                if ((firstHistory != null && firstHistory.Time.RealTime == null) || (secondHistory != null && secondHistory.Time.RealTime == null))
                {
                    firstSegment.SegmentHistory.Remove(firstHistory);
                    secondSegment.SegmentHistory.Remove(secondHistory);
                }
            }

            var comparisonKeys = new List<String>(firstSegment.Comparisons.Keys);
            foreach (var comparison in comparisonKeys)
            {
                var previousTime = segIndex > 0 ? SegmentList.ElementAt(segIndex - 1).Comparisons[comparison] : new Time(TimeSpan.Zero, TimeSpan.Zero);
                var firstSegmentTime = firstSegment.Comparisons[comparison] - previousTime;
                var secondSegmentTime = secondSegment.Comparisons[comparison] - firstSegment.Comparisons[comparison];
                secondSegment.Comparisons[comparison] = new Time(previousTime+secondSegmentTime);
                firstSegment.Comparisons[comparison] = new Time(secondSegment.Comparisons[comparison]+firstSegmentTime);
            }

            SegmentList.Remove(secondSegment);
            SegmentList.Insert(segIndex, secondSegment);

            UpdateButtonsStatus();
        }

        private void FixAfterDeletion(int index)
        {
            FixWithTimingMethod(index, TimingMethod.RealTime);
            FixWithTimingMethod(index, TimingMethod.GameTime);
        }

        private void FixWithTimingMethod(int index, TimingMethod method)
        {
            var curIndex = index + 1;

            if (curIndex < Run.Count)
            {
                for (var runIndex = Run.GetMinSegmentHistoryIndex(); runIndex <= Run.RunHistory.Count; runIndex++)
                {
                    curIndex = index + 1;
                    var segmentHistoryElement = Run[index].SegmentHistory.Where(x => x.Index == runIndex).FirstOrDefault();
                    if (segmentHistoryElement == null)
                    {
                        var nextSegment = Run[curIndex].SegmentHistory.Where(x => x.Index == runIndex).FirstOrDefault();
                        if (nextSegment != null)
                            Run[curIndex].SegmentHistory.Remove(nextSegment);
                        continue;
                    }

                    var curSegment = segmentHistoryElement.Time[method];
                    while (curSegment != null && curIndex < Run.Count)
                    {
                        var segment = Run[curIndex].SegmentHistory.Where(x => x.Index == runIndex).FirstOrDefault();
                        if (segment != null && segment.Time[method] != null)
                        {
                            var time = segment.Time;
                            time[method] = curSegment + segment.Time[method];
                            segment.Time = time;
                            break;
                        }
                        curIndex++;
                    }
                }

                curIndex = index + 1;

                var curSegmentTime = Run[index].BestSegmentTime[method];
                var nextSegmentTime = Run[curIndex].BestSegmentTime[method];
                var minBestSegment = curSegmentTime + nextSegmentTime;
                foreach (var history in Run[curIndex].SegmentHistory)
                {
                    if (history.Time[method] < minBestSegment)
                        minBestSegment = history.Time[method];
                }
                var time2 = Run[curIndex].BestSegmentTime;
                time2[method] = minBestSegment;
                Run[curIndex].BestSegmentTime = time2;
            }
        }

        private void tbxGameName_TextChanged(object sender, EventArgs e)
        {
            RaiseRunEdited();
        }

        private void tbxRunCategory_TextChanged(object sender, EventArgs e)
        {
            RaiseRunEdited();
        }

        private void tbxTimeOffset_TextChanged(object sender, EventArgs e)
        {
            if (IsInitialized)
                Run.HasChanged = true;
        }

        private void tbxAttempts_TextChanged(object sender, EventArgs e)
        {
            RaiseRunEdited();
        }

        private void RaiseRunEdited()
        {
            if (IsInitialized)
            {
                Run.HasChanged = true;
                if (RunEdited != null)
                {
                    RunEdited(this, null);
                }
            }
        }

        private void RunEditorDialog_Load(object sender, EventArgs e)
        {
            RebuildComparisonColumns();
            IsInitialized = true;
            UpdateButtonsStatus();
        }

        private void picGameIcon_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                RemoveIconMenu.Show(this, e.Location);
            }
        }

        private void removeIconToolStripMenuItem_Click(object sender, EventArgs e)
        {
            GameIcon = null;
            picGameIcon.Image = GameIcon;
            RaiseRunEdited();
        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            picGameIcon_DoubleClick(null,null);
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            RemoveSelected();
        }

        private void downloadBoxartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                var gameId = PBTracker.Instance.GetGameIdByName(cbxGameName.Text);

                GameIcon = PBTracker.Instance.GetGameBoxArt(gameId);
                picGameIcon.Image = GameIcon;
                RaiseRunEdited();
            }
            catch (Exception ex)
            {
                Log.Error(ex);

                try
                {
                    GameIcon = Twitch.Instance.GetGameBoxArt(cbxGameName.Text);
                    picGameIcon.Image = GameIcon;
                    RaiseRunEdited();
                }
                catch (Exception exc)
                {
                    Log.Error(exc);

                    MessageBox.Show("Could not download the box art of the game!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void openFromURLMenuItem_Click(object sender, EventArgs e)
        {
            string url = null;

            if (DialogResult.OK == InputBox.Show("Open Game Icon from URL", "URL:", ref url))
            {
                try
                {
                    var uri = new Uri(url);

                    var request = (HttpWebRequest)HttpWebRequest.Create(uri);
                    using (var stream = request.GetResponse().GetResponseStream())
                    {
                        try
                        {
                            GameIcon = Image.FromStream(stream);
                            picGameIcon.Image = GameIcon;
                            RaiseRunEdited();
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex);
                            MessageBox.Show("The url was not recognized as an image.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    MessageBox.Show("The Game Icon couldn't be downloaded.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RebuildComparisonColumns()
        {
            foreach (var comparison in Run.CustomComparisons)
            {
                if (comparison != LiveSplit.Model.Run.PersonalBestComparisonName)
                    AddComparisonColumn(comparison);
            }
        }

        private void AddComparisonColumn(String name)
        {
            var column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.Width = Math.Max(100, column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.ColumnHeader, true));
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            var rightClickMenu = new ContextMenuStrip();
            var renameItem = new ToolStripMenuItem("Rename");
            renameItem.Click += (s, e) =>
            {
                RenameComparison(column);
            };
            var removeItem = new ToolStripMenuItem("Remove");
            removeItem.Click += (s, e) =>
            {
                RemoveComparison(column);
            };
            rightClickMenu.Items.Add(renameItem);
            rightClickMenu.Items.Add(removeItem);
            column.HeaderCell.ContextMenuStrip = rightClickMenu;
            runGrid.Columns.Add(column);
            RaiseRunEdited();
        }

        private void RenameComparison(DataGridViewTextBoxColumn column)
        {
            var name = column.Name;
            var newName = name;
            var dialogResult = InputBox.Show("Rename Comparison", "Comparison Name:", ref newName);
            if (dialogResult == System.Windows.Forms.DialogResult.OK)
            {
                if (!Run.Comparisons.Contains(newName))
                {
                    if (!newName.StartsWith("[Race]"))
                    {
                        column.Name = newName;
                        column.Width = Math.Max(100, column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.ColumnHeader, true));
                        if (CurrentState.CurrentComparison == name)
                            CurrentState.CurrentComparison = newName;
                        Run.CustomComparisons[Run.CustomComparisons.IndexOf(name)] = newName;
                        foreach (var segment in Run)
                        {
                            segment.Comparisons[newName] = segment.Comparisons[name];
                            segment.Comparisons.Remove(name);
                        }
                        var args = new RenameEventArgs();
                        args.OldName = name;
                        args.NewName = newName;
                        ComparisonRenamed(this, args);
                    }
                    else
                    {
                        var result = MessageBox.Show(this, "A Comparison name cannot start with [Race].", "Invalid Comparison Name", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                        if (result == System.Windows.Forms.DialogResult.Retry)
                            RenameComparison(column);
                    }
                }
                else if (newName != name)
                {
                    var result = MessageBox.Show(this, "A Comparison with this name already exists.", "Comparison Already Exists", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                    if (result == System.Windows.Forms.DialogResult.Retry)
                        RenameComparison(column);
                }
            }
            RaiseRunEdited();
        }

        private void RemoveComparison(DataGridViewTextBoxColumn column)
        {
            var name = column.Name;
            runGrid.Columns.Remove(column);
            Run.CustomComparisons.Remove(name);

            if (CurrentState.CurrentComparison == name)
                CurrentState.CurrentComparison = LiveSplit.Model.Run.PersonalBestComparisonName;

            var args = new RenameEventArgs();
            args.OldName = name;
            args.NewName = "Current Comparison";
            ComparisonRenamed(this, args);

            foreach (var segment in Run)
                segment.Comparisons.Remove(name);
            RaiseRunEdited();
        }

        private void btnAddComparison_Click(object sender, EventArgs e)
        {
            String name = "";
            var result = InputBox.Show("New Comparison", "Comparison Name:", ref name);
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                if (!Run.Comparisons.Contains(name))
                {
                    if (!name.StartsWith("[Race]"))
                    {
                        AddComparisonColumn(name);
                        Run.CustomComparisons.Add(name);
                    }
                    else
                    {
                        result = MessageBox.Show(this, "A Comparison name cannot start with [Race].", "Invalid Comparison Name", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                        if (result == System.Windows.Forms.DialogResult.Retry)
                            btnAddComparison_Click(sender, e);
                    }
                }
                else
                {
                    result = MessageBox.Show(this, "A Comparison with this name already exists.", "Comparison Already Exists", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                    if (result == System.Windows.Forms.DialogResult.Retry)
                        btnAddComparison_Click(sender, e);
                }
            }
        }

        private void TabSelected(object sender, TabControlEventArgs e)
        {
            UpdateSegmentList();
            runGrid.Invalidate();
        }

        protected void RefreshAutoSplittingUI()
        {
            lblDescription.Text = CurrentState.Run.AutoSplitter == null 
                ? "There is no Auto Splitter available for this game."
                : CurrentState.Run.AutoSplitter.Description;
            btnActivate.Text = CurrentState.Run.IsAutoSplitterActive()
                ? "Deactivate"
                : "Activate";
            btnActivate.Enabled = CurrentState.Run.AutoSplitter != null;
            btnSettings.Enabled = CurrentState.Run.IsAutoSplitterActive() && CurrentState.Run.AutoSplitter.Component.GetSettingsControl(LayoutMode.Vertical) != null;
        }

        private void btnActivate_Click(object sender, EventArgs e)
        {
            if (CurrentState.Run.AutoSplitter.IsActivated)
            {
                CurrentState.Settings.ActiveAutoSplitters.Remove(CurrentState.Run.GameName);
                CurrentState.Run.AutoSplitter.Deactivate();
            }
            else
            {
                CurrentState.Settings.ActiveAutoSplitters.Add(CurrentState.Run.GameName);
                CurrentState.Run.AutoSplitter.Activate(CurrentState);
            }
            RefreshAutoSplittingUI();
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            var dialog = new ComponentSettingsDialog(CurrentState.Run.AutoSplitter.Component);
            var result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var document = new XmlDocument();
                var autoSplitterSettings = document.CreateElement("AutoSplitterSettings");
                autoSplitterSettings.InnerXml = Run.AutoSplitter.Component.GetSettings(document).InnerXml;
                var gameName = document.CreateAttribute("gameName");
                gameName.Value = Run.GameName;
                autoSplitterSettings.Attributes.Append(gameName);
                Run.AutoSplitterSettings = autoSplitterSettings;
            }
        }

        private void btnMoveUp_Click(object sender, EventArgs e)
        {
            var selectedCell = runGrid.SelectedCells.Cast<DataGridViewCell>().ToList().FirstOrDefault();
            if (selectedCell != null)
            {
                var selectedInd = selectedCell.RowIndex;
                if (selectedInd > 0)
                {
                    var prevIndex = runGrid.CurrentRow.Index;
                    SwitchSegments(selectedInd - 1);
                    runGrid.CurrentCell = runGrid.Rows[prevIndex - 1].Cells[runGrid.CurrentCell.ColumnIndex];
                    Fix();
                }
            }
        }

        private void btnMoveDown_Click(object sender, EventArgs e)
        {
            var selectedCell = runGrid.SelectedCells.Cast<DataGridViewCell>().ToList().FirstOrDefault();
            if (selectedCell != null)
            {
                var selectedInd = selectedCell.RowIndex;
                if (selectedInd < SegmentList.Count - 1)
                {
                    var prevIndex = runGrid.CurrentRow.Index;
                    SwitchSegments(selectedInd);
                    runGrid.CurrentCell = runGrid.Rows[prevIndex + 1].Cells[runGrid.CurrentCell.ColumnIndex];
                    Fix();
                }
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BrightIdeasSoftware;
using FreelancerModStudio.Controls;
using FreelancerModStudio.Data;
using FreelancerModStudio.Data.INI;
using FreelancerModStudio.Data.IO;
using FreelancerModStudio.Properties;
using FreelancerModStudio.SystemPresenter;
using FreelancerModStudio.SystemPresenter.Content;
using WeifenLuo.WinFormsUI.Docking;
using Clipboard = FreelancerModStudio.Data.Clipboard;

namespace FreelancerModStudio
{
    public partial class frmTableEditor : DockContent, IDocumentForm
    {
        public TableData Data;
        public string File { get; private set; }
        public string DataPath { get; private set; }

        readonly bool _isBINI;

        readonly UndoManager<ChangedData> _undoManager = new UndoManager<ChangedData>();

        public ViewerType ViewerType { get; set; }
        public ArchetypeManager Archetype { get; set; }

        public delegate void DataChangedType(ChangedData data);

        public DataChangedType DataChanged;

        public delegate void SelectionChangedType(List<TableBlock> data, int templateIndex);

        public SelectionChangedType SelectionChanged;

        public delegate void DataVisibilityChangedType(TableBlock block);

        public DataVisibilityChangedType DataVisibilityChanged;

        public delegate void DocumentChangedType(IDocumentForm document);

        public DocumentChangedType DocumentChanged;

        void OnDataChanged(ChangedData data)
        {
            if (DataChanged != null)
            {
                DataChanged(data);
            }
        }

        void OnSelectionChanged(List<TableBlock> data, int templateIndex)
        {
            if (SelectionChanged != null)
            {
                SelectionChanged(data, templateIndex);
            }
        }

        void OnDataVisibilityChanged(TableBlock block)
        {
            if (DataVisibilityChanged != null)
            {
                DataVisibilityChanged(block);
            }
        }

        void OnDocumentChanged(IDocumentForm document)
        {
            if (DocumentChanged != null)
            {
                DocumentChanged(document);
            }
        }

        public frmTableEditor(int templateIndex, string file)
        {
            InitializeComponent();

            LoadIcons();
            _undoManager.DataChanged += UndoManager_DataChanged;

            if (file != null)
            {
                FileManager fileManager = new FileManager(file)
                    {
                        ReadWriteComments = true, // always read comments
                    };
                EditorINIData iniContent = fileManager.Read(FileEncoding.Automatic, templateIndex);

                Data = new TableData(iniContent);
                _isBINI = fileManager.IsBINI;

                SetFile(file);
            }
            else
            {
                Data = new TableData
                    {
                        TemplateIndex = templateIndex
                    };

                SetFile(string.Empty);
            }

            objectListView1.CellToolTip.InitialDelay = 1000;
            objectListView1.UnfocusedHighlightBackgroundColor = objectListView1.HighlightBackgroundColorOrDefault;
            objectListView1.UnfocusedHighlightForegroundColor = objectListView1.HighlightForegroundColorOrDefault;

            SimpleDropSink dropSink = objectListView1.DropSink as SimpleDropSink;
            if (dropSink != null)
            {
                dropSink.CanDropBetween = true;
                dropSink.CanDropOnItem = false;
            }

            RefreshSettings();
        }

        void LoadIcons()
        {
            Icon = Resources.FileINIIcon;

            // synchronized with ContentType enum
            ImageList imageList = new ImageList
                {
                    ColorDepth = ColorDepth.Depth32Bit
                };
            imageList.Images.AddRange(new Image[]
                {
                    Resources.System,
                    Resources.LightSource,
                    Resources.Construct,
                    Resources.Depot,
                    Resources.DockingRing,
                    Resources.JumpGate,
                    Resources.JumpHole,
                    Resources.Planet,
                    Resources.Satellite,
                    Resources.Ship,
                    Resources.Station,
                    Resources.Sun,
                    Resources.TradeLane,
                    Resources.WeaponsPlatform,
                    Resources.Zone,
                    Resources.ZoneCylinder,
                    Resources.ZoneBox,
                    Resources.ZoneExclusion,
                    Resources.ZoneCylinderExclusion,
                    Resources.ZoneBoxExclusion,
                    Resources.ZoneVignette,
                    Resources.ZonePath,
                    Resources.ZonePathTrade,
                    Resources.ZonePathTradeLane
                });
            objectListView1.SmallImageList = imageList;
        }

        public void RefreshSettings()
        {
            objectListView1.EmptyListMsg = Strings.FileEditorEmpty;

            //display modified rows in different color
            objectListView1.RowFormatter = delegate(OLVListItem lvi)
                {
                    TableBlock block = (TableBlock)lvi.RowObject;
                    switch (block.Modified)
                    {
                        case TableModified.ChangedAdded:
                            lvi.BackColor = Helper.Settings.Data.Data.General.EditorModifiedAddedColor;
                            break;
                        case TableModified.Changed:
                            lvi.BackColor = Helper.Settings.Data.Data.General.EditorModifiedColor;
                            break;
                        case TableModified.ChangedSaved:
                            lvi.BackColor = Helper.Settings.Data.Data.General.EditorModifiedSavedColor;
                            break;
                    }

                    if (ViewerType == ViewerType.System && block.ObjectType != ContentType.None && !block.Visibility)
                    {
                        lvi.ForeColor = Helper.Settings.Data.Data.General.EditorHiddenColor;
                    }
                };

            //refresh column text
            if (objectListView1.Columns.Count > 2)
            {
                objectListView1.Columns[0].Text = Strings.FileEditorColumnName;
                objectListView1.Columns[2].Text = Strings.FileEditorColumnType;
            }

            //update 'New file' to new language
            //also needed to reset title after culture changer changed it
            SetFile(File);

            objectListView1.Refresh();
        }

        string ShowSolarArchetypeSelector()
        {
            OpenFileDialog openFile = new OpenFileDialog
                {
                    Title = string.Format(Strings.FileEditorOpenSolarArch, GetFileName()),
                    Filter = "Solar Archetype INI|*.ini"
                };
            if (openFile.ShowDialog() == DialogResult.OK)
            {
                return openFile.FileName;
            }

            return null;
        }

        void LoadArchetypes()
        {
            string archetypeFile = ArchetypeManager.GetRelativeArchetype(File, Data.TemplateIndex);

            // try to fallback to default DATA path
            if (archetypeFile == null)
            {
                archetypeFile = ArchetypeManager.GetRelativeArchetype(Helper.Settings.Data.Data.General.DefaultDataDirectory);

                //user interaction required to get the path of the archetype file
                if (archetypeFile == null)
                {
                    archetypeFile = ShowSolarArchetypeSelector();
                }
            }

            //set data path based on archetype file and not system file
            DataPath = Helper.Template.Data.GetDataPath(archetypeFile, Helper.Template.Data.SolarArchetypeFile);

            if (Archetype == null)
            {
                Archetype = new ArchetypeManager(archetypeFile, Helper.Template.Data.SolarArchetypeFile);
            }

            SetAllBlockTypes();
        }

        void SetAllBlockTypes()
        {
            foreach (TableBlock block in Data.Blocks)
            {
                SetBlockType(block);
                block.SetVisibleIfPossible();
            }
        }

        void SetBlockType(TableBlock block)
        {
            switch (ViewerType)
            {
                case ViewerType.System:
                    SystemParser.SetObjectType(block, Archetype);
                    break;
                case ViewerType.Universe:
                    SystemParser.SetUniverseObjectType(block);
                    break;
                case ViewerType.SolarArchetype:
                    SystemParser.SetSolarArchetypeObjectType(block);
                    break;
                case ViewerType.ModelPreview:
                    SystemParser.SetModelPreviewObjectType(block);
                    break;
            }
        }

        void SetViewerType()
        {
            if (Data.TemplateIndex == Helper.Template.Data.SystemFile)
            {
                ViewerType = ViewerType.System;
            }
            else if (Data.TemplateIndex == Helper.Template.Data.UniverseFile)
            {
                ViewerType = ViewerType.Universe;
            }
            else if (Data.TemplateIndex == Helper.Template.Data.SolarArchetypeFile)
            {
                ViewerType = ViewerType.SolarArchetype;
            }
            else if (Data.TemplateIndex == Helper.Template.Data.AsteroidArchetypeFile ||
                     Data.TemplateIndex == Helper.Template.Data.ShipArchetypeFile ||
                     Data.TemplateIndex == Helper.Template.Data.EquipmentFile ||
                     Data.TemplateIndex == Helper.Template.Data.EffectExplosionsFile)
            {
                ViewerType = ViewerType.ModelPreview;
            }
            else
            {
                ViewerType = ViewerType.None;
            }
        }

        public void ShowData()
        {
            SetViewerType();
            switch (ViewerType)
            {
                case ViewerType.SolarArchetype:
                case ViewerType.ModelPreview:
                    DataPath = Helper.Template.Data.GetDataPath(File, Helper.Template.Data.SolarArchetypeFile);
                    SetAllBlockTypes();
                    break;
                case ViewerType.System:
                case ViewerType.Universe:
                    LoadArchetypes();
                    break;
            }

#if DEBUG
            Stopwatch st = new Stopwatch();
            st.Start();
#endif
            AddColumns();

            objectListView1.SetObjects(Data.Blocks);

            //add block types to add menu
            for (int i = 0; i < Helper.Template.Data.Files[Data.TemplateIndex].Blocks.Count; ++i)
            {
                ToolStripMenuItem addItem = new ToolStripMenuItem
                    {
                        Text = Helper.Template.Data.Files[Data.TemplateIndex].Blocks.Values[i].Name,
                        Tag = i
                    };
                addItem.Click += mnuAddItem_Click;
                mnuAdd.DropDownItems.Add(addItem);
            }
#if DEBUG
            st.Stop();
            Debug.WriteLine("display " + objectListView1.Items.Count + " data: " + st.ElapsedMilliseconds + "ms");
#endif
        }

        void AddColumns()
        {
            //clear all items and columns
            objectListView1.Clear();

            objectListView1.CheckBoxes = ViewerType == ViewerType.System;

            OLVColumn[] cols =
                {
                    new OLVColumn(Strings.FileEditorColumnName, "Name"),
                    new OLVColumn("#", "ID"),
                    new OLVColumn(Strings.FileEditorColumnType, "Group")
                };

            cols[0].Width = 150;

            cols[1].Width = 36;

            cols[2].MinimumWidth = 120;
            cols[2].FillsFreeSpace = true;

            if (ViewerType != ViewerType.None)
            {
                // content type icons
                cols[0].ImageGetter = delegate(object x)
                    {
                        // basic model + system and ellipsoid + sphere and cylinder + ring share the same icon
                        ContentType type = ((TableBlock)x).ObjectType;
                        if (type == ContentType.ModelPreview)
                        {
                            return 0;
                        }
                        if (type >= ContentType.ZoneEllipsoidExclusion)
                        {
                            return (int)type - 4;
                        }
                        if (type >= ContentType.ZoneRing)
                        {
                            return (int)type - 3;
                        }
                        if (type >= ContentType.ZoneEllipsoid)
                        {
                            return (int)type - 2;
                        }
                        return (int)type - 1;
                    };
            }

            if (ViewerType == ViewerType.System)
            {
                cols[0].AspectGetter = x => ((TableBlock)x).Name;

                //checkboxes for hidden shown objects
                objectListView1.BooleanCheckStateGetter = x => ((TableBlock)x).Visibility;
                objectListView1.BooleanCheckStatePutter = delegate(object x, bool newValue)
                    {
                        TableBlock block = (TableBlock)x;
                        if (block.ObjectType != ContentType.None)
                        {
                            block.Visibility = newValue;
                            OnDataVisibilityChanged(block);
                            return newValue;
                        }

                        return block.Visibility;
                    };
            }
            else
            {
                objectListView1.BooleanCheckStateGetter = null;
                objectListView1.BooleanCheckStatePutter = null;
            }

            if (ViewerType == ViewerType.System || ViewerType == ViewerType.SolarArchetype)
            {
                //show content type if possible otherwise group
                cols[2].AspectGetter = delegate(object x)
                    {
                        TableBlock block = (TableBlock)x;
                        if (block.ObjectType != ContentType.None)
                        {
                            return block.ObjectType.ToString();
                        }

                        return block.Group;
                    };
            }

            //show ID + 1
            cols[1].AspectGetter = x => ((TableBlock)x).Index + 1;

            //show all options of a block in the tooltip
            objectListView1.CellToolTipGetter = (col, x) => ((TableBlock)x).ToolTip;

            objectListView1.Columns.AddRange(cols);
        }

        void objectListView1_SelectionChanged(object sender, EventArgs e)
        {
            OnSelectionChanged(GetSelectedBlocks(), Data.TemplateIndex);
            OnDocumentChanged(this);
        }

        void Save(string file)
        {
            FileManager fileManager = new FileManager(file, _isBINI)
                {
                    WriteSpaces = Helper.Settings.Data.Data.General.FormattingSpaces,
                    WriteEmptyLine = Helper.Settings.Data.Data.General.FormattingEmptyLine,
                    ReadWriteComments = Helper.Settings.Data.Data.General.FormattingComments,
                };
            fileManager.Write(Data.GetEditorData());

            SetAsSaved();

            try
            {
                SetFile(file);
            }
            catch (Exception ex)
            {
                Helper.Exceptions.Show(ex);
            }
        }

        void SetFile(string file)
        {
            File = file;
            ToolTipText = File;

            string title = GetTitle();
            if (_undoManager.IsModified())
            {
                title += "*";
            }

            if (Text == title)
            {
                return;
            }

            Text = title;
            TabText = title;

            OnDocumentChanged(this);
        }

        void SetAsSaved()
        {
            if (_undoManager.IsModified())
            {
                _undoManager.SetAsSaved();

                //set objects in listview as unmodified
                foreach (TableBlock tableData in objectListView1.Objects)
                {
                    if (tableData.Modified == TableModified.Changed ||
                        tableData.Modified == TableModified.ChangedAdded)
                    {
                        tableData.Modified = TableModified.ChangedSaved;
                        objectListView1.RefreshObject(tableData);
                    }
                }
            }
        }

        bool CancelClose()
        {
            if (_undoManager.IsModified())
            {
                DialogResult dialogResult = MessageBox.Show(string.Format(Strings.FileCloseSave, GetTitle()), Helper.Assembly.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (dialogResult == DialogResult.Cancel)
                {
                    return true;
                }
                if (dialogResult == DialogResult.Yes)
                {
                    Save();
                }
            }

            return false;
        }

        void BlocksAdded(List<TableBlock> blocks)
        {
            List<TableBlock> selectedData = new List<TableBlock>();
            foreach (TableBlock block in blocks)
            {
                if (block.Index >= Data.Blocks.Count)
                {
                    Data.Blocks.Add(block);
                }
                else
                {
                    Data.Blocks.Insert(block.Index, block);
                }

                selectedData.Add(block);
            }

            Data.RefreshIndices(blocks[0].Index);

            objectListView1.SetObjects(Data.Blocks);
            objectListView1.SelectedObjects = selectedData;
            EnsureSelectionVisible();
        }

        void AddBlock(string blockName, int templateIndex)
        {
            Template.Block templateBlock = Helper.Template.Data.Files[Data.TemplateIndex].Blocks.Values[templateIndex];

            //add options to new block
            EditorINIBlock editorBlock = new EditorINIBlock(blockName, templateIndex);
            for (int i = 0; i < templateBlock.Options.Count; ++i)
            {
                Template.Option option = templateBlock.Options[i];
                editorBlock.Options.Add(new EditorINIOption(option.Name, i));

                if (templateBlock.Identifier != null && templateBlock.Identifier.Equals(editorBlock.Options[editorBlock.Options.Count - 1].Name, StringComparison.OrdinalIgnoreCase))
                {
                    editorBlock.MainOptionIndex = editorBlock.Options.Count - 1;
                    editorBlock.Options[editorBlock.Options.Count - 1].Values.Add(new EditorINIEntry(blockName));
                }
            }

            //add actual block
            AddBlocks(new List<TableBlock>
                {
                    new TableBlock(GetNewBlockId(), Data.MaxId++, editorBlock, Data.TemplateIndex)
                });
        }

        public List<TableBlock> GetSelectedBlocks()
        {
            if (objectListView1.SelectedObjects.Count == 0)
            {
                return null;
            }

            List<TableBlock> blocks = new List<TableBlock>();
            foreach (TableBlock tableData in objectListView1.SelectedObjects)
            {
                blocks.Add(tableData);
            }

            return blocks;
        }

        public void ChangeBlocks(PropertyBlock[] propertyBlocks)
        {
            List<TableBlock> newBlocks = new List<TableBlock>();
            List<TableBlock> oldBlocks = new List<TableBlock>();

            for (int i = 0; i < propertyBlocks.Length; ++i)
            {
                TableBlock oldBlock = (TableBlock)objectListView1.SelectedObjects[i];
                TableBlock newBlock = ObjectClone.Clone(oldBlock);

                oldBlocks.Add(oldBlock);
                newBlocks.Add(newBlock);

                PropertyBlock propertyBlock = propertyBlocks[i];

                // set comments (last property option)
                string comments = (string)propertyBlock[propertyBlock.Count - 1].Value;
                newBlock.Block.Comments = comments;

                // set options
                for (int j = 0; j < propertyBlock.Count - 1; ++j)
                {
                    List<EditorINIEntry> options = newBlock.Block.Options[j].Values;

                    PropertySubOptions propertyOptions = propertyBlock[j].Value as PropertySubOptions;
                    if (propertyOptions != null)
                    {
                        options.Clear();

                        //loop all sub values in the sub value collection
                        foreach (PropertyOption value in propertyOptions)
                        {
                            string text = ((string)value.Value).Trim();
                            if (text.Length != 0)
                            {
                                if (text.Contains(Environment.NewLine))
                                {
                                    string[] lines = text.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                                    List<object> subOptions = new List<object>();
                                    for (int k = 1; k < lines.Length; ++k)
                                    {
                                        subOptions.Add(lines[k].Trim());
                                    }

                                    options.Add(new EditorINIEntry(lines[0], subOptions));
                                }
                                else
                                {
                                    options.Add(new EditorINIEntry(text));
                                }
                            }
                        }
                    }
                    else
                    {
                        string text = ((string)propertyBlock[j].Value).Trim();
                        if (text.Length != 0)
                        {
                            if (options.Count > 0)
                            {
                                //check if value is different
                                if (options[0].Value.ToString() != text)
                                {
                                    options[0].Value = text;
                                }
                            }
                            else
                            {
                                options.Add(new EditorINIEntry(text));
                            }
                        }
                        else
                        {
                            options.Clear();
                        }

                        //change data in listview
                        if (newBlock.Block.MainOptionIndex == j)
                        {
                            newBlock.Name = text;
                        }
                    }
                }

                // update block object type
                SetBlockType(newBlock);

                // make block visible if it can be made visible now
                if (oldBlock.ObjectType == ContentType.None)
                {
                    newBlock.SetVisibleIfPossible();
                }

                // mark block as modified
                newBlock.SetModifiedChanged();
            }

            ChangeBlocks(newBlocks, oldBlocks);
        }

        public void ChangeBlocks(List<TableBlock> newBlocks, List<TableBlock> oldBlocks)
        {
            _undoManager.Execute(new ChangedData
                {
                    NewBlocks = newBlocks,
                    OldBlocks = oldBlocks,
                    Type = ChangedType.Edit
                });
        }

        void BlocksChanged(List<TableBlock> newBlocks, List<TableBlock> oldBlocks)
        {
            for (int i = 0; i < oldBlocks.Count; ++i)
            {
                int index = Data.Blocks.IndexOf(oldBlocks[i]);
                Data.Blocks[index] = newBlocks[i];
            }

            objectListView1.SetObjects(Data.Blocks);
            objectListView1.RefreshObjects(Data.Blocks);

            //select objects which were selected before
            objectListView1.SelectObjects(newBlocks);
            EnsureSelectionVisible();
        }

        void BlocksMoved(List<TableBlock> newBlocks, List<TableBlock> oldBlocks)
        {
            //remove all moved blocks first because otherwise inserted index would be wrong
            List<TableBlock> blocks = new List<TableBlock>();
            for (int i = oldBlocks.Count - 1; i >= 0; i--)
            {
                blocks.Add(Data.Blocks[oldBlocks[i].Index]);
                Data.Blocks.RemoveAt(oldBlocks[i].Index);
            }

            //insert blocks at new position
            for (int i = 0; i < oldBlocks.Count; ++i)
            {
                Data.Blocks.Insert(newBlocks[i].Index, blocks[oldBlocks.Count - i - 1]);
            }

            Data.RefreshIndices(Math.Min(oldBlocks[0].Index, newBlocks[0].Index));
            objectListView1.SetObjects(Data.Blocks);
            objectListView1.RefreshObjects(Data.Blocks);

            //select objects which were selected before
            objectListView1.SelectObjects(blocks);
            EnsureSelectionVisible();
        }

        void BlocksDeleted(List<TableBlock> blocks)
        {
            IList selectedObjects = objectListView1.SelectedObjects;

            foreach (TableBlock tableBlock in blocks)
            {
                Data.Blocks.Remove(tableBlock);
            }

            Data.RefreshIndices(blocks[0].Index);
            objectListView1.RemoveObjects(blocks);

            //select objects which were selected before
            objectListView1.SelectObjects(selectedObjects);
            EnsureSelectionVisible();
        }

        void DeleteSelectedBlocks()
        {
            List<TableBlock> blocks = new List<TableBlock>();

            foreach (TableBlock block in objectListView1.SelectedObjects)
            {
                blocks.Add(block);
            }

            _undoManager.Execute(new ChangedData
                {
                    NewBlocks = blocks,
                    Type = ChangedType.Delete
                });
        }

        void EnsureSelectionVisible()
        {
            if (objectListView1.SelectedObjects.Count > 0)
            {
                objectListView1.EnsureVisible(objectListView1.IndexOf(objectListView1.SelectedObjects[objectListView1.SelectedObjects.Count - 1]));
                objectListView1.EnsureVisible(objectListView1.IndexOf(objectListView1.SelectedObjects[0]));
            }
        }

        void frmDefaultEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = CancelClose();
        }

        public void SelectAll()
        {
            objectListView1.SelectAll();
        }

        void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            SetContextMenuEnabled();
        }

        void SetContextMenuEnabled()
        {
            bool active = CanCut();
            mnuCut.Visible = active;
            mnuCut.Enabled = active;

            active = CanCopy();
            mnuCopy.Visible = active;
            mnuCopy.Enabled = active;

            mnuPaste.Enabled = CanPaste();

            active = CanDelete();
            mnuDelete.Visible = active;
            mnuDelete.Enabled = active;
        }

        void mnuAddItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = (ToolStripMenuItem)sender;
            AddBlock(menuItem.Text, (int)menuItem.Tag);
        }

        void mnuDelete_Click(object sender, EventArgs e)
        {
            DeleteSelectedBlocks();
        }

        void mnuCut_Click(object sender, EventArgs e)
        {
            Cut();
        }

        void mnuCopy_Click(object sender, EventArgs e)
        {
            Copy();
        }

        void mnuPaste_Click(object sender, EventArgs e)
        {
            Paste();
        }

        public bool CanSave()
        {
            return true;
        }

        public bool CanCopy()
        {
            return objectListView1.SelectedObjects.Count > 0;
        }

        public bool CanCut()
        {
            return objectListView1.SelectedObjects.Count > 0;
        }

        public bool CanPaste()
        {
            return Clipboard.CanPaste(typeof(EditorINIData));
        }

        public bool CanAdd()
        {
            return true;
        }

        public bool CanDelete()
        {
            return objectListView1.SelectedObjects.Count > 0;
        }

        public bool CanSelectAll()
        {
            return true;
        }

        public void Save()
        {
            if (File.Length == 0)
            {
                SaveAs();
            }
            else
            {
                Save(File);
            }
        }

        public void SaveAs()
        {
            SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = Strings.FileDialogFilter
                };
            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                Save(saveDialog.FileName);
            }
        }

        public void Delete()
        {
            DeleteSelectedBlocks();
        }

        public ToolStripDropDown MultipleAddDropDown()
        {
            return mnuAdd.DropDown;
        }

        string GetFileName()
        {
            if (File.Length == 0)
            {
                return File;
            }

            return Path.GetFileName(File);
        }

        public string GetTitle()
        {
            return File.Length == 0 ? Strings.FileEditorNewFile : Path.GetFileName(File);
        }

        public void Copy()
        {
            EditorINIData data = new EditorINIData(Data.TemplateIndex);
            foreach (TableBlock tableData in objectListView1.SelectedObjects)
            {
                data.Blocks.Add(tableData.Block);
            }

            Clipboard.Copy(data, typeof(EditorINIData));

            OnDocumentChanged(this);
        }

        public void Cut()
        {
            Copy();
            DeleteSelectedBlocks();
        }

        public void Paste()
        {
            EditorINIData editorData = (EditorINIData)Clipboard.Paste(typeof(EditorINIData));

            if (editorData.TemplateIndex == Data.TemplateIndex)
            {
                int id = GetNewBlockId();

                List<TableBlock> blocks = new List<TableBlock>();
                for (int i = 0; i < editorData.Blocks.Count; ++i)
                {
                    blocks.Add(new TableBlock(id + i, Data.MaxId++, editorData.Blocks[i], Data.TemplateIndex));
                }

                AddBlocks(blocks);
            }
        }

        void AddBlocks(List<TableBlock> blocks)
        {
            List<TableBlock> newAdditionalBlocks = new List<TableBlock>();
            List<TableBlock> oldBlocks = new List<TableBlock>();

            for (int i = 0; i < blocks.Count; ++i)
            {
                TableBlock block = blocks[i];

                //set block to be modified
                block.Modified = TableModified.ChangedAdded;

                //set archetype of block and make visible if possible
                if (block.Archetype == null)
                {
                    SetBlockType(block);
                    block.SetVisibleIfPossible();
                }

                //check if block which can only exist once already exists
                Template.Block templateBlock = Helper.Template.Data.Files[Data.TemplateIndex].Blocks.Values[block.Block.TemplateIndex];
                if (!templateBlock.Multiple)
                {
                    for (int j = 0; j < Data.Blocks.Count; ++j)
                    {
                        TableBlock existingBlock = Data.Blocks[j];

                        //block already exists
                        if (existingBlock.Block.TemplateIndex == block.Block.TemplateIndex)
                        {
                            block.Index = existingBlock.Index;
                            block.Id = existingBlock.Id;

                            //overwrite block if it can only exist once
                            newAdditionalBlocks.Add(block);
                            oldBlocks.Add(existingBlock);

                            //remove overwritten block from new ones
                            blocks.RemoveAt(i);
                            --i;
                        }
                    }
                }
            }

            if (newAdditionalBlocks.Count == 0)
            {
                _undoManager.Execute(new ChangedData
                    {
                        Type = ChangedType.Add,
                        NewBlocks = blocks,
                    });
            }
            else
            {
                if (blocks.Count == 0)
                {
                    _undoManager.Execute(new ChangedData
                        {
                            Type = ChangedType.Edit,
                            NewBlocks = newAdditionalBlocks,
                            OldBlocks = oldBlocks,
                        });
                }
                else
                {
                    _undoManager.Execute(new ChangedData
                        {
                            Type = ChangedType.AddAndEdit,
                            NewBlocks = blocks,
                            NewAdditionalBlocks = newAdditionalBlocks,
                            OldBlocks = oldBlocks,
                        });
                }
            }
        }

        public bool CanUndo()
        {
            return _undoManager.CanUndo();
        }

        public bool CanRedo()
        {
            return _undoManager.CanRedo();
        }

        public void Undo()
        {
            _undoManager.Undo(1);
        }

        public void Redo()
        {
            _undoManager.Redo(1);
        }

        public bool CanDisplay3DViewer()
        {
            return ViewerType != ViewerType.None;
        }

        public bool CanManipulatePosition()
        {
            return ViewerType == ViewerType.System || ViewerType == ViewerType.Universe;
        }

        public bool CanManipulateRotationScale()
        {
            return ViewerType == ViewerType.System;
        }

        void ExecuteDataChanged(ChangedData data, bool undo)
        {
            switch (data.Type)
            {
                case ChangedType.Add:
                    BlocksAdded(data.NewBlocks);
                    break;
                case ChangedType.Edit:
                    BlocksChanged(data.NewBlocks, data.OldBlocks);
                    break;
                case ChangedType.Move:
                    BlocksMoved(data.NewBlocks, data.OldBlocks);
                    break;
                case ChangedType.Delete:
                    BlocksDeleted(data.NewBlocks);
                    break;
                case ChangedType.AddAndEdit:
                    BlocksAdded(data.NewBlocks);
                    BlocksChanged(data.NewAdditionalBlocks, data.OldBlocks);
                    break;
                case ChangedType.DeleteAndEdit:
                    BlocksDeleted(data.NewBlocks);
                    BlocksChanged(data.NewAdditionalBlocks, data.OldBlocks);
                    break;
            }

            OnDataChanged(data);
        }

        void UndoManager_DataChanged(ChangedData data, bool undo)
        {
            ExecuteDataChanged(undo ? data.GetUndoData() : data, undo);

            SetFile(File);
            //OnDocumentChanged(this); is already called in SetFile
        }

        public void Select(TableBlock block, bool toggle)
        {
            if (toggle)
            {
                IList selectedObjects = objectListView1.SelectedObjects;

                if (objectListView1.IsSelected(block))
                {
                    selectedObjects.Remove(block);
                }
                else
                {
                    selectedObjects.Add(block);
                }

                objectListView1.SelectedObjects = selectedObjects;
            }
            else
            {
                objectListView1.SelectedObject = block;
            }
            EnsureSelectionVisible();
        }

        public void SelectItemIndex(int value)
        {
            objectListView1.SelectedIndex = value;
            EnsureSelectionVisible();
        }

        public void Select(int id)
        {
            int itemIndex = 0;
            foreach (TableBlock block in objectListView1.Objects)
            {
                if (block.Id == id)
                {
                    SelectItemIndex(itemIndex);
                    return;
                }
                ++itemIndex;
            }
        }

        //overwrite to add extra information to layout.xml
        protected override string GetPersistString()
        {
            return typeof(frmTableEditor) + "," + File + "," + Data.TemplateIndex;
        }

        public void HideShowSelected()
        {
            IList selectedObjects = objectListView1.SelectedObjects;
            if (selectedObjects.Count == 0)
            {
                return;
            }

            bool visibility = !((TableBlock)selectedObjects[0]).Visibility;

            foreach (TableBlock block in selectedObjects)
            {
                if (block.ObjectType != ContentType.None && block.Visibility != visibility)
                {
                    block.Visibility = visibility;
                    OnDataVisibilityChanged(block);
                }
            }

            objectListView1.RefreshObjects(selectedObjects);
        }

        public bool CanChangeVisibility(bool rightNow)
        {
            bool correctFileType = ViewerType == ViewerType.System;
            if (rightNow)
            {
                return correctFileType && objectListView1.SelectedObjects.Count > 0;
            }

            return correctFileType;
        }

        public bool CanFocusSelected(bool rightNow)
        {
            bool correctFileType = ViewerType != ViewerType.None;
            if (rightNow)
            {
                return correctFileType && objectListView1.SelectedObjects.Count > 0;
            }

            return correctFileType;
        }

        public bool CanTrackSelected(bool rightNow)
        {
            bool correctFileType = ViewerType == ViewerType.System;
            if (rightNow)
            {
                return correctFileType && objectListView1.SelectedObjects.Count > 0;
            }

            return correctFileType;
        }

        public void ChangeVisibility()
        {
            HideShowSelected();
        }

        void objectListView1_CanDrop(object sender, OlvDropEventArgs e)
        {
            if (e.DropTargetItem.RowObject is TableBlock)
            {
                e.Effect = DragDropEffects.Move;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        void objectListView1_Dropped(object sender, OlvDropEventArgs e)
        {
            OLVDataObject o = e.DataObject as OLVDataObject;
            if (o != null)
            {
                List<TableBlock> blocks = new List<TableBlock>();
                foreach (TableBlock block in o.ModelObjects)
                {
                    blocks.Add(block);
                }

                MoveBlocks(blocks, e.DropTargetIndex);
            }
        }

        void MoveBlocks(List<TableBlock> blocks, int targetIndex)
        {
            List<TableBlock> oldBlocks = new List<TableBlock>();
            List<TableBlock> newBlocks = new List<TableBlock>();

            for (int i = 0; i < blocks.Count; ++i)
            {
                //calculate correct insert position
                int newIndex = targetIndex + i;

                //decrease index if old blocks id is lower than the new index because they will be deleted first
                for (int j = i - newBlocks.Count; j < blocks.Count; ++j)
                {
                    if (blocks[j].Index < newIndex)
                    {
                        newIndex--;
                    }
                }

                //skip block if the id was not changed
                if (blocks[i].Index != newIndex)
                {
                    newBlocks.Add(new TableBlock(newIndex, 0));
                    oldBlocks.Add(new TableBlock(blocks[i].Index, 0));
                }
            }

            if (oldBlocks.Count > 0)
            {
                _undoManager.Execute(new ChangedData
                    {
                        NewBlocks = newBlocks,
                        OldBlocks = oldBlocks,
                        Type = ChangedType.Move
                    });
            }
        }

        int GetNewBlockId()
        {
            //add new block under selected one if it exists otherwise at the end
            if (objectListView1.SelectedIndices.Count > 0)
            {
                return objectListView1.SelectedIndices[objectListView1.SelectedIndices.Count - 1] + 1;
            }

            return Data.Blocks.Count;
        }
    }
}

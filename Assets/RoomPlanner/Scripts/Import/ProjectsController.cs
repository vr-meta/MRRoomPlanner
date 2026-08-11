using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Core.Project;
using RoomPlanner.Measure;
using RoomPlanner.Tools;

namespace RoomPlanner.Import
{
    /// <summary>
    /// Project management tool ("Proj", #58, design/06 «Проекты v1»): named projects in
    /// ProjectPaths.Root, a Select row lists them (the open one is marked •), and the
    /// actions cover the whole lifecycle — Save allocates "Project N" on first save,
    /// Open autosaves the current scene first so switching never loses work, New starts
    /// an empty unnamed scene, Delete removes the selected file (hold — destructive).
    /// Import is import-only again: its "New project" row moved here.
    /// </summary>
    public class ProjectsController : MonoBehaviour, ITool
    {
        [SerializeField] private ToolManager manager;
        [SerializeField] private MeasureInput input;
        [SerializeField] private ImportController import;
        [SerializeField] private ProjectAutosave autosave;

        private SettingsSchema _settings;
        private readonly List<string> _names = new();
        private int _index;
        private string _status = "";

        public string Id => "projects";
        public string PaletteLabel => "Proj";
        public string IconId => "folder";

        public SettingsSchema GetSettings()
        {
            _settings ??= new SettingsSchema()
                .Readout("cur", "Current",
                    () => ProjectPaths.HasCurrent ? ProjectPaths.CurrentName : "Unsaved")
                .Select("proj", "Project", Options, () => Mathf.Max(0, _index), Pick)
                .Action("open", "Open", "folder", Open)
                .Action("save", "Save", "check", Save)
                .Action("new", "New project", "plus", New, destructive: true)
                .Action("del", "Delete selected", "trash", Delete, destructive: true)
                .Readout("status", "Status", () => _status);
            return _settings;
        }

        public void OnActivate()
        {
            Refresh();
            // land the selection on the open project so Open/Delete start meaningful
            int cur = _names.IndexOf(ProjectPaths.CurrentName);
            if (cur >= 0) _index = cur;
            _status = "";
        }

        public void OnDeactivate() { }

        public void Tick(bool blocked)
        {
            if (blocked || input == null) return;
            // B = Esc back to Select (UX v2 P0.3) — the tool has no gesture input.
            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        // ---- catalog ----

        private void Refresh()
        {
            _names.Clear();
            _names.AddRange(ProjectPaths.ListNames());
            _index = _names.Count == 0 ? 0 : Mathf.Clamp(_index, 0, _names.Count - 1);
        }

        private string[] Options()
        {
            Refresh();
            if (_names.Count == 0) return new[] { "no projects" };
            var arr = new string[_names.Count];
            for (int i = 0; i < _names.Count; i++)
                arr[i] = _names[i] == ProjectPaths.CurrentName ? _names[i] + " •" : _names[i];
            return arr;
        }

        private void Pick(int index) => _index = index;

        private string SelectedName =>
            _names.Count > 0 ? _names[Mathf.Clamp(_index, 0, _names.Count - 1)] : null;

        // ---- lifecycle actions ----

        /// <summary>Open the selected project. The current scene autosaves first —
        /// switching projects must never lose work.</summary>
        public void Open()
        {
            Refresh();
            var name = SelectedName;
            if (name == null) { _status = "no projects"; return; }
            if (name == ProjectPaths.CurrentName) { _status = "already open"; return; }
            if (autosave == null) { _status = "no autosave rig"; return; }

            autosave.Save(ProjectPaths.CurrentSavePath);
            if (autosave.TryLoad(ProjectPaths.PathFor(name)))
            {
                ProjectPaths.CurrentName = name;
                _status = $"opened {name}";
            }
            else _status = "open failed";
        }

        /// <summary>Save the scene into the open project; the first save of an unnamed
        /// scene allocates the next free "Project N".</summary>
        public void Save()
        {
            if (autosave == null) { _status = "no autosave rig"; return; }
            bool wasUnnamed = !ProjectPaths.HasCurrent;
            string name = wasUnnamed
                ? ProjectCatalog.NextName(ProjectPaths.ListNames())
                : ProjectPaths.CurrentName;
            if (autosave.Save(ProjectPaths.PathFor(name)))
            {
                ProjectPaths.CurrentName = name;
                Refresh();
                int i = _names.IndexOf(name);
                if (i >= 0) _index = i;
                _status = $"saved {name}";
            }
            else _status = "empty scene — nothing to save";
        }

        /// <summary>Empty unnamed scene. Named project files stay on disk — only the
        /// unnamed autosave is wiped (ImportController.NewProject).</summary>
        public void New()
        {
            if (import != null) import.NewProject();
            ProjectPaths.CurrentName = "";
            _status = "new project";
        }

        /// <summary>Delete the selected project file (+its backup). Deleting the open
        /// project keeps the scene but makes it unnamed.</summary>
        public void Delete()
        {
            Refresh();
            var name = SelectedName;
            if (name == null) { _status = "no projects"; return; }
            string path = ProjectPaths.PathFor(name);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                string bak = ProjectFileIO.BackupPath(path);
                if (File.Exists(bak)) File.Delete(bak);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Projects] delete failed: {e.Message}");
                _status = "delete failed";
                return;
            }
            if (name == ProjectPaths.CurrentName) ProjectPaths.CurrentName = "";
            Refresh();
            _status = $"deleted {name}";
        }
    }
}

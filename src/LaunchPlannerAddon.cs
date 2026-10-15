using System;
using KSP.UI.Screens;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>Fenêtre du planificateur dans l'éditeur (VAB et SPH), ouverte par un bouton de la barre d'outils.</summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class LaunchPlannerAddon : MonoBehaviour
    {
        const int WindowId = 0x4C504C31;
        static readonly string[] Tabs = { "Mission", "Lanceurs", "Pièces disponibles" };

        ApplicationLauncherButton button;
        bool buttonFailed;
        bool visible;
        Rect windowRect = new Rect(100, 70, 1000, 620);
        int tab;
        readonly MissionView mission = new MissionView();
        readonly PartsView parts = new PartsView();
        LauncherView launchers;

        void Awake()
        {
            launchers = new LauncherView(mission);
        }

        void Start()
        {
            try
            {
                PartInventory.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                parts.ReportError("Erreur à la lecture des pièces : " + e.Message);
            }
            try
            {
                mission.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void Update()
        {
            if (visible)
            {
                try { mission.Update(); }
                catch (Exception e) { Debug.LogException(e); }
                // Le calcul des lanceurs ne tourne que lorsque l'onglet est affiché.
                try { if (tab == 1) launchers.Update(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (button != null || buttonFailed) return;
            try
            {
                TryAddButton();
            }
            catch (Exception e)
            {
                // Si la barre d'outils n'est pas accessible, on affiche quand même la fenêtre.
                buttonFailed = true;
                visible = true;
                Debug.LogException(e);
            }
        }

        void TryAddButton()
        {
            ApplicationLauncher launcher = Refl.GetStatic(typeof(ApplicationLauncher), "Instance") as ApplicationLauncher;
            object ready = Refl.GetStatic(typeof(ApplicationLauncher), "Ready");
            if (launcher == null || !(ready is bool) || !(bool)ready) return;
            button = launcher.AddModApplication(
                () => visible = true, () => visible = false, null, null, null, null,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH, MakeIcon());
        }

        void OnDestroy()
        {
            if (editorLocked) SetEditorLock(false);
            if (button == null) return;
            try
            {
                ApplicationLauncher launcher = Refl.GetStatic(typeof(ApplicationLauncher), "Instance") as ApplicationLauncher;
                if (launcher != null) launcher.RemoveModApplication(button);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        const string LockId = "LaunchPlannerWindow";
        bool editorLocked;

        void OnGUI()
        {
            if (visible) windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Planificateur de lanceur");
            // Tant que la souris est sur la fenêtre, l'éditeur ignore les clics (sinon ils prennent ou posent des pièces derrière).
            bool over = visible && windowRect.Contains(Event.current.mousePosition);
            if (over != editorLocked) SetEditorLock(over);
        }

        void SetEditorLock(bool locked)
        {
            editorLocked = locked;
            try
            {
                EditorLogic editor = EditorLogic.fetch;
                if (editor == null) return;
                if (locked) editor.Lock(false, false, false, LockId);
                else editor.Unlock(LockId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            tab = GUILayout.Toolbar(tab, Tabs, GUILayout.Width(420));
            GUILayout.FlexibleSpace();
            if (buttonFailed && GUILayout.Button("Fermer", GUILayout.Width(70))) visible = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (tab == 0) mission.Draw();
            else if (tab == 1) launchers.Draw();
            else parts.Draw();
            GUI.DragWindow();
        }

        static Texture2D MakeIcon()
        {
            // Icône 38x38 dessinée à la main : une fusée blanche sur fond transparent.
            const int size = 38;
            Texture2D tex = new Texture2D(size, size);
            Color clear = new Color(0, 0, 0, 0);
            Color white = new Color(1, 1, 1, 1);
            Color flame = new Color(1f, 0.6f, 0.1f, 1);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    Color c = clear;
                    int dx = Math.Abs(x - size / 2);
                    if (y >= 10 && y <= 28 && dx <= 4) c = white;                 // corps
                    else if (y > 28 && y <= 34 && dx <= 34 - y) c = white;        // coiffe
                    else if (y >= 8 && y <= 14 && dx > 4 && dx <= 8) c = white;   // ailerons
                    else if (y >= 3 && y < 10 && dx <= (y - 3) / 2) c = flame;    // flamme
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }
    }
}

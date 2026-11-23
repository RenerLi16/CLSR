using System;
using System.Collections.Generic;

namespace CLSR.Presentation
{
    /// <summary>Engine-free rectangle in IMGUI screen coordinates (origin top-left, y down).</summary>
    public readonly struct RectF
    {
        public RectF(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public float XMax => X + Width;
        public float YMax => Y + Height;

        public bool Contains(float px, float py) => px >= X && px < XMax && py >= Y && py < YMax;

        public bool Overlaps(RectF other) =>
            X < other.XMax && XMax > other.X && Y < other.YMax && YMax > other.Y;

        public bool Inside(float screenWidth, float screenHeight) =>
            X >= -0.01f && Y >= -0.01f && XMax <= screenWidth + 0.01f && YMax <= screenHeight + 0.01f;

        public override string ToString() => $"({X:0.#},{Y:0.#} {Width:0.#}x{Height:0.#})";
    }

    /// <summary>
    /// Tactical HUD regions around a full-screen map: menu and mission status
    /// top-left, day/phase/timer top-centre, minimap top-right, a vertical tool
    /// strip on the left edge, location details bottom-centre and the dispatch
    /// command area bottom-right.
    /// </summary>
    public sealed class TacticalLayout
    {
        public TacticalLayout(float screenWidth, float screenHeight)
        {
            Width = screenWidth;
            Height = screenHeight;
            float s = Scale = Clamp(Math.Min(screenWidth / 1024f, screenHeight / 576f), .72f, 2.5f);

            float minimap = Clamp(190f * s, 150f, 380f);
            float commandWidth = Clamp(151f * s, 126f, 360f);
            float commandHeight = Clamp(120f * s, 102f, 286f);

            MenuButton = new RectF(0, 0, 50f * s, 23f * s);
            Objectives = new RectF(0, MenuButton.YMax + 2f * s, Clamp(150f * s, 132f, 260f), 120f * s);
            MenuDropdown = new RectF(0, MenuButton.YMax, 178f * s, 78f * s);
            Timer = new RectF(screenWidth * .5f - 49f * s, 0, 98f * s, 31f * s);
            float noticeHalf = Math.Min(175f * s, screenWidth * .28f);
            Notice = new RectF(screenWidth * .5f - noticeHalf, 57f * s, Math.Min(350f * s, screenWidth * .56f), 21f * s);
            Minimap = new RectF(screenWidth - minimap, 0, minimap, minimap);
            CameraStrip = new RectF(Minimap.X, Minimap.YMax, Minimap.Width, 24f * s);

            float interventionWidth = Math.Min(520f * s, screenWidth * .64f);
            Intervention = new RectF((screenWidth - interventionWidth) * .5f, 84f * s, interventionWidth, 70f * s);

            float toolbarHeight = 4f * 38f * s;
            float toolbarMin = Objectives.YMax + 18f;
            float toolbarMax = Math.Max(toolbarMin, screenHeight - toolbarHeight - 12f);
            Toolbar = new RectF(0, Clamp(275f * s, toolbarMin, toolbarMax), 56f * s, toolbarHeight);

            Command = new RectF(screenWidth - commandWidth, screenHeight - commandHeight, commandWidth, commandHeight);
            float planHeight = 44f * s;
            PlanStrip = new RectF(Command.X, Command.Y - planHeight, Command.Width, planHeight);

            float gap = 10f * s;
            float inspectorHeight = Clamp(100f * s, 86f, 170f);
            float inspectorWidth = Math.Min(523f * s, screenWidth - Command.Width - 2f * gap);
            float inspectorX = Clamp(250f * s, Toolbar.XMax + 4f * s, Command.X - gap - inspectorWidth);
            Inspector = new RectF(inspectorX, screenHeight - inspectorHeight, inspectorWidth, inspectorHeight);
        }

        public float Width { get; }
        public float Height { get; }
        public float Scale { get; }
        public RectF MenuButton { get; }
        public RectF MenuDropdown { get; }
        public RectF Objectives { get; }
        public RectF Timer { get; }
        public RectF Notice { get; }
        public RectF Intervention { get; }
        public RectF Minimap { get; }
        public RectF CameraStrip { get; }
        public RectF Toolbar { get; }
        public RectF Inspector { get; }
        public RectF PlanStrip { get; }
        public RectF Command { get; }

        /// <summary>Panels that are always on screen during planning, by name.</summary>
        public IEnumerable<KeyValuePair<string, RectF>> PersistentPanels()
        {
            yield return new KeyValuePair<string, RectF>("menu", MenuButton);
            yield return new KeyValuePair<string, RectF>("objectives", Objectives);
            yield return new KeyValuePair<string, RectF>("timer", Timer);
            yield return new KeyValuePair<string, RectF>("notice", Notice);
            yield return new KeyValuePair<string, RectF>("intervention", Intervention);
            yield return new KeyValuePair<string, RectF>("minimap", Minimap);
            yield return new KeyValuePair<string, RectF>("camera", CameraStrip);
            yield return new KeyValuePair<string, RectF>("toolbar", Toolbar);
            yield return new KeyValuePair<string, RectF>("inspector", Inspector);
            yield return new KeyValuePair<string, RectF>("plan", PlanStrip);
            yield return new KeyValuePair<string, RectF>("command", Command);
        }

        public bool IsOverHud(float x, float y, bool interventionVisible, bool menuOpen)
        {
            foreach (KeyValuePair<string, RectF> panel in PersistentPanels())
            {
                if (panel.Key == "intervention" && !interventionVisible) continue;
                if (panel.Key == "notice") continue; // Transient banner; never blocks the map.
                if (panel.Value.Contains(x, y)) return true;
            }
            return menuOpen && MenuDropdown.Contains(x, y);
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}

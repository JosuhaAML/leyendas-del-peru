using System.Collections.Generic;
using UnityEngine;

// Estilo visual andino de la interfaz: paneles y marcos dorados con chakana, barras con brillo,
// botones y tipografía Georgia. Los gráficos están en Assets/Resources/UI (Herramientas/generar_ui.py).
// Solo se usa dentro de OnGUI.
public static class UIStyle
{
    public static readonly Color Gold = new Color(0.95f, 0.78f, 0.42f);
    public static readonly Color Cream = new Color(0.96f, 0.92f, 0.84f);

    static bool ready;
    static Texture2D barFill, white;
    static GUIStyle panel, barFrame, slot, title, text, small, button, buttonActive, buttonDanger, banner;
    static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    static Font serif;

    public static GUIStyle Title { get { Init(); return title; } }
    public static GUIStyle Text { get { Init(); return text; } }
    public static GUIStyle Small { get { Init(); return small; } }
    public static GUIStyle Button { get { Init(); return button; } }
    public static GUIStyle ButtonActive { get { Init(); return buttonActive; } }
    public static GUIStyle ButtonDanger { get { Init(); return buttonDanger; } }
    public static readonly Color DarkText = new Color(0.22f, 0.11f, 0.03f);

    static void Init()
    {
        if (ready) return;
        ready = true;
        white = Texture2D.whiteTexture;
        barFill = Resources.Load<Texture2D>("UI/barra_relleno");
        serif = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Palatino Linotype", "Times New Roman" }, 18);

        panel = Framed("UI/panel", 26);
        slot = Framed("UI/ranura", 14);
        barFrame = Framed("UI/barra_marco", 16);
        barFrame.border = new RectOffset(16, 16, 8, 8);
        banner = Framed("UI/boton_rojo", 26);
        banner.border = new RectOffset(28, 28, 14, 14);

        title = new GUIStyle(GUI.skin.label)
        {
            font = serif, fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true,
            normal = { textColor = Gold },
        };
        text = new GUIStyle(GUI.skin.label)
        {
            font = serif, fontSize = 16, richText = true, wordWrap = true,
            normal = { textColor = Cream },
        };
        small = new GUIStyle(text) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false };

        button = new GUIStyle(GUI.skin.button)
        {
            font = serif, fontSize = 15, fontStyle = FontStyle.Bold, richText = true,
            border = new RectOffset(28, 28, 14, 14),
            padding = new RectOffset(30, 30, 4, 4),
            normal = { background = Load("UI/boton"), textColor = Cream },
            hover = { background = Load("UI/boton_hover"), textColor = Color.white },
            active = { background = Load("UI/boton_activo"), textColor = DarkText },
        };
        buttonActive = new GUIStyle(button)
        {
            normal = { background = Load("UI/boton_activo"), textColor = DarkText },
            hover = { background = Load("UI/boton_activo_hover"), textColor = DarkText },
        };
        buttonDanger = new GUIStyle(button)
        {
            normal = { background = Load("UI/boton_rojo"), textColor = Cream },
            hover = { background = Load("UI/boton_rojo_hover"), textColor = Color.white },
        };
    }

    public static Texture2D Tex(string name) => Load("UI/" + name);

    // Separador dorado con rombos (centrado en r).
    public static void Separator(Rect r)
    {
        var t = Tex("separador");
        if (t != null) GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
    }

    // Esquinas con grecas en los 4 bordes de la pantalla (w = ancho virtual, alto 720).
    public static void ScreenCorners(float w, float size = 96f)
    {
        var t = Tex("esquina");
        if (t == null) return;
        GUI.DrawTextureWithTexCoords(new Rect(0, 0, size, size), t, new Rect(0, 1, 1, -1));
        GUI.DrawTextureWithTexCoords(new Rect(w - size, 0, size, size), t, new Rect(1, 1, -1, -1));
        GUI.DrawTextureWithTexCoords(new Rect(0, 720 - size, size, size), t, new Rect(0, 0, 1, 1));
        GUI.DrawTextureWithTexCoords(new Rect(w - size, 720 - size, size, size), t, new Rect(1, 0, -1, 1));
    }

    // Botón con ícono a la izquierda del texto.
    public static bool IconButton(Rect r, string icon, string label, GUIStyle style, int fontSize = 0)
    {
        Init();
        var s = new GUIStyle(style);
        if (fontSize > 0) s.fontSize = fontSize;
        var tex = Tex("icono_" + icon);
        float iconSize = Mathf.Min(r.height * 0.55f, 34f);
        float textW = s.CalcSize(new GUIContent(label)).x - s.padding.horizontal;
        bool pressed = GUI.Button(r, GUIContent.none, s);
        float total = iconSize + 12 + textW;
        float x = r.center.x - total / 2;
        if (tex != null)
        {
            var c = GUI.color;
            GUI.color = s == buttonActive || style == buttonActive ? new Color(0.3f, 0.15f, 0.04f, c.a) : new Color(1, 0.92f, 0.75f, c.a);
            GUI.DrawTexture(new Rect(x, r.center.y - iconSize / 2, iconSize, iconSize), tex);
            GUI.color = c;
        }
        var labelStyle = new GUIStyle(s) { normal = { background = null, textColor = s.normal.textColor }, hover = { background = null }, padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(x + iconSize + 12, r.y, textW + 10, r.height), label, labelStyle);
        return pressed;
    }

    // Aviso rojo con puntas (mecánicas del jefe).
    public static void Banner(Rect r)
    {
        Init();
        GUI.Box(r, GUIContent.none, banner);
    }

    static Texture2D Load(string path) => Resources.Load<Texture2D>(path);

    static GUIStyle Framed(string path, int border) => new GUIStyle
    {
        normal = { background = Load(path) },
        border = new RectOffset(border, border, border, border),
    };

    public static void Panel(Rect r)
    {
        Init();
        GUI.Box(r, GUIContent.none, panel);
        // Adorno dorado arriba en los paneles grandes.
        if (r.width >= 300 && r.height >= 150 && r.y > 20)
        {
            var orn = Tex("adorno");
            if (orn != null) GUI.DrawTexture(new Rect(r.center.x - 48, r.y - 14, 96, 32), orn);
        }
    }

    // Barra de vida: fondo oscuro, relleno teñido con brillo y marco dorado encima.
    public static void Bar(Rect r, float fill, Color color, string label = null)
    {
        Init();
        // Fondo y relleno dentro del marco con puntas.
        var inner = new Rect(r.x + 9, r.y + 5, r.width - 18, r.height - 10);
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(inner, white);
        GUI.color = color;
        GUI.DrawTexture(new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(fill), inner.height), barFill);
        GUI.color = Color.white;
        GUI.Box(r, GUIContent.none, barFrame);
        if (label != null) GUI.Label(r, label, small);
    }

    // Ranura de habilidad: ícono teñido, tecla y sombra de recarga que baja.
    public static void AbilitySlot(Rect r, string iconName, Color color, string key, float cooldownFrac, string bottom)
    {
        Init();
        GUI.Box(r, GUIContent.none, slot);
        var icon = Icon(iconName);
        var inner = new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 20);
        if (icon != null)
        {
            GUI.color = cooldownFrac > 0f ? color * 0.6f : color;
            GUI.DrawTexture(inner, icon, ScaleMode.ScaleToFit);
        }
        if (cooldownFrac > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(r.x + 4, r.y + 4, r.width - 8, (r.height - 8) * cooldownFrac), white);
        }
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x + 6, r.y + 2, 40, 18), $"<b>{key}</b>", new GUIStyle(small) { alignment = TextAnchor.UpperLeft });
        GUI.Label(new Rect(r.x, r.yMax - 20, r.width, 18), bottom, small);
    }

    public static Texture2D Icon(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!icons.TryGetValue(name, out var tex)) icons[name] = tex = Load("UI/icono_" + name);
        return tex;
    }

    // Ícono de cada habilidad según su nombre.
    public static string IconFor(string ability) => ability switch
    {
        "Golpe de maza" => "maza",
        "Tajo de cóndor" => "condor",
        "Furia del Inti" or "Toque de luz" => "sol",
        "Escudazo" => "escudo",
        "Provocar" => "grito",
        "Muro de piedra" => "muro",
        "Flecha" => "flecha",
        "Flecha certera" => "diana",
        "Lluvia de flechas" => "lluvia",
        "Rayo" => "rayo",
        "Fuego de ayahuasca" or "Espíritu del jaguar" => "fuego",
        "Salto de cóndor" => "condor",
        "Golpe rápido" => "maza",
        "Empujón de escudo" => "escudo",
        "Disparo rápido" => "flecha",
        "Chispa" => "rayo",
        "Destello" => "sol",
        "Temblor de la Pachamama" => "muro",
        "Flecha de Illapa" => "rayo",
        "Bendición del Inti" => "sol",
        _ => "cura",
    };
}

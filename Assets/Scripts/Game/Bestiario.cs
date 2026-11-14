using UnityEngine;

// Bestiario: las cinco leyendas con su historia, de dónde vienen, un dato real y cómo vencerlas.
// Se abre desde el menú principal. Las ya vencidas muestran el sello dorado.
public static class Bestiario
{
    public static bool Open;
    static int sel;
    public static void Select(int i) => sel = Mathf.Clamp(i, 0, Entries.Length - 1);

    struct Entry { public string name, epithet, region, legend, fact, tip; }

    static readonly Entry[] Entries =
    {
        new Entry {
            name = "Chullachaqui", epithet = "El guardián del monte",
            region = "Amazonía peruana · Loreto, Ucayali y Madre de Dios",
            legend = "Espíritu dueño del bosque. Tiene un pie humano y el otro de animal o torcido, y por eso camina cojeando. Toma la forma de un amigo o de un familiar para engañar a quien entra al monte y llevarlo hasta perderlo entre los árboles.",
            fact = "Su nombre viene del quechua: «chulla» (impar, desigual) y «chaki» (pie). Muchos cazadores le piden permiso al bosque antes de entrar, para no ofenderlo.",
            tip = "Se multiplica en réplicas que se mueven igual que él: solo el verdadero pierde vida. Sal de los círculos de raíces y apártate de su embestida." },
        new Entry {
            name = "Yacumama", epithet = "La madre de las aguas",
            region = "Ríos y cochas de la Amazonía peruana",
            legend = "Serpiente gigantesca que vive en el fondo de lagunas y ríos. Se dice que es la madre y protectora de las aguas, que con su aliento atrae hacia el agua a personas y animales, y que cuando se mueve forma remolinos.",
            fact = "«Yacu» significa agua y «mama», madre, en quechua. La leyenda se relaciona con la anaconda verde, una de las serpientes más grandes y pesadas del mundo, que vive en la Amazonía.",
            tip = "Se sumerge y reaparece en otro lugar: sigue las burbujas. Al emerger queda aturdida unos segundos, ¡ataquen con todo! Esquiva sus escupitajos de agua." },
        new Entry {
            name = "Bufeo Colorado", epithet = "El encantador del río",
            region = "Ríos Amazonas, Ucayali y Marañón",
            legend = "Delfín rosado que en noches de fiesta sale del río convertido en un joven elegante vestido de blanco, con sombrero para esconder el orificio de su cabeza. Encanta a las muchachas y se las lleva al fondo del río.",
            fact = "El bufeo colorado existe: es el delfín rosado de río (Inia geoffrensis), el delfín de río más grande del mundo. Es una especie protegida y amenazada por la pesca y la contaminación.",
            tip = "Su encantamiento te arrastra hacia él: camina en sentido contrario. Cuando marque una línea, apártate: va a embestir por ahí." },
        new Entry {
            name = "Tunche", epithet = "El espíritu que silba en la noche",
            region = "Selva peruana",
            legend = "Alma en pena que anuncia su llegada con un silbido agudo y largo. Dicen que si el silbido suena lejos, en realidad está cerca, y que visita a quien le responde o se burla de él.",
            fact = "Es una de las leyendas más contadas en la selva peruana. Mucha gente relaciona su silbido con el canto de aves nocturnas que se escuchan en el monte de noche.",
            tip = "Cuando silba, corre a ponerte junto a una vela encendida: su luz te protege. Fuera de las velas, el silbido hace daño a todos." },
        new Entry {
            name = "Pishtaco", epithet = "El degollador de los Andes",
            region = "Andes del Perú · Ayacucho, Huancavelica, Cusco",
            legend = "Forastero alto y misterioso que ataca de noche a los viajeros en los caminos solitarios de los Andes para robarles la grasa del cuerpo. Se esconde en las sombras y nadie lo ve venir.",
            fact = "El mito aparece en relatos desde la época colonial. Su nombre viene del quechua «pishtay», que significa degollar o cortar en tiras.",
            tip = "Desaparece entre las sombras y reaparece detrás de alguien: si ves el aviso, muévete. No se queden todos juntos y cuídense las espaldas." },
    };

    public static void Draw(float w, Texture2D[] portraits)
    {
        // Atajos: flechas para cambiar, Esc para cerrar.
        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape) { Open = false; e.Use(); return; }
            if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.RightArrow) { sel = (sel + 1) % Entries.Length; e.Use(); }
            if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.LeftArrow) { sel = (sel + Entries.Length - 1) % Entries.Length; e.Use(); }
        }

        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(0, 0, w, 720), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var r = new Rect(w / 2 - 600, 20, 1200, 680);
        UIStyle.Panel(r);
        GUI.Label(new Rect(r.x, r.y + 12, r.width, 44), "BESTIARIO", new GUIStyle(UIStyle.Title) { fontSize = 34 });
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 24), "Las cinco leyendas del Perú", new GUIStyle(UIStyle.Text) { fontSize = 16, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter });
        UIStyle.Separator(new Rect(r.center.x - 200, r.y + 76, 400, 18));

        // Lista a la izquierda.
        for (int i = 0; i < Entries.Length; i++)
        {
            var b = new Rect(r.x + 30, r.y + 110 + i * 104, 250, 94);
            bool on = i == sel;
            GUI.Box(b, GUIContent.none, on ? UIStyle.ButtonActive : UIStyle.Button);
            if (portraits != null && i < portraits.Length && portraits[i] != null)
                Cover(new Rect(b.x + 8, b.y + 8, 78, 78), portraits[i]);
            var ns = new GUIStyle(UIStyle.Text) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = on ? UIStyle.DarkText : UIStyle.Gold } };
            GUI.Label(new Rect(b.x + 96, b.y + 10, 150, 40), Entries[i].name, ns);
            var ss = new GUIStyle(UIStyle.Small) { fontSize = 12, alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = on ? UIStyle.DarkText : UIStyle.Cream } };
            GUI.Label(new Rect(b.x + 96, b.y + 46, 150, 40), Progress.IsBeaten(i) ? "Vencido" : $"Nivel {i + 1}", ss);
            if (GUI.Button(b, GUIContent.none, GUIStyle.none)) sel = i;
        }

        // Ficha a la derecha.
        var en = Entries[sel];
        var img = new Rect(r.x + 310, r.y + 110, 360, 500);
        GUI.Box(new Rect(img.x - 6, img.y - 6, img.width + 12, img.height + 12), GUIContent.none, UIStyle.Button);
        if (portraits != null && sel < portraits.Length && portraits[sel] != null) Cover(img, portraits[sel]);
        if (Progress.IsBeaten(sel))
        {
            GUI.DrawTexture(new Rect(img.xMax - 70, img.yMax - 70, 60, 60), UIStyle.Tex("chakana"));
            GUI.Label(new Rect(img.x, img.yMax - 36, img.width - 80, 30), "VENCIDO", new GUIStyle(UIStyle.Title) { fontSize = 22, alignment = TextAnchor.MiddleRight });
        }

        float tx = r.x + 700, tw = r.xMax - tx - 34, y = r.y + 104;
        GUI.Label(new Rect(tx, y, tw, 48), en.name.ToUpper(), new GUIStyle(UIStyle.Title) { fontSize = 38, alignment = TextAnchor.MiddleLeft });
        GUI.Label(new Rect(tx, y + 46, tw, 26), en.epithet, new GUIStyle(UIStyle.Text) { fontSize = 19, fontStyle = FontStyle.Italic });
        GUI.Label(new Rect(tx, y + 74, tw, 22), en.region, new GUIStyle(UIStyle.Small) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = UIStyle.Cream } });
        y += 106;
        y = Section(tx, y, tw, "La leyenda", en.legend, UIStyle.Gold);
        y = Section(tx, y, tw, "Dato real", en.fact, new Color(0.55f, 0.85f, 1f));
        Section(tx, y, tw, "Cómo vencerlo", en.tip, new Color(1f, 0.6f, 0.45f));

        if (UIStyle.IconButton(new Rect(r.center.x - 110, r.yMax - 58, 220, 42), "salir", "Cerrar", UIStyle.ButtonDanger, 18)) Open = false;
        GUI.Label(new Rect(r.x + 30, r.yMax - 52, 260, 30), "Flechas: cambiar · Esc: cerrar", new GUIStyle(UIStyle.Small) { fontSize = 12, alignment = TextAnchor.MiddleLeft });
    }

    static float Section(float x, float y, float w, string title, string body, Color color)
    {
        GUI.Label(new Rect(x, y, w, 24), title.ToUpper(), new GUIStyle(UIStyle.Text) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = color } });
        GUI.color = new Color(color.r, color.g, color.b, 0.5f);
        GUI.DrawTexture(new Rect(x, y + 24, w, 1), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var bs = new GUIStyle(UIStyle.Text) { fontSize = 15, wordWrap = true, alignment = TextAnchor.UpperLeft };
        float h = bs.CalcHeight(new GUIContent(body), w);
        GUI.Label(new Rect(x, y + 30, w, h), body, bs);
        return y + 30 + h + 14;
    }

    // Dibuja la imagen llenando el rectángulo sin deformarla (recorta lo que sobra).
    public static void Cover(Rect r, Texture tex)
    {
        float ar = (float)tex.width / tex.height, rr = r.width / r.height;
        var uv = ar > rr ? new Rect((1f - rr / ar) / 2f, 0, rr / ar, 1) : new Rect(0, (1f - ar / rr) / 2f, 1, ar / rr);
        GUI.DrawTextureWithTexCoords(r, tex, uv);
    }
}

// Créditos finales que suben por la pantalla, con su canción (Resources/Audio/creditos).
// Al terminar el juego salen solos (no es una opción) y se pueden saltar con clic, Esc o Espacio.
// Los textos se leen de LeyendasDelPeru_Data/StreamingAssets/creditos.txt (editable sin compilar).
// Líneas que empiezan con # son títulos de sección; la primera línea es el título grande.
public static class Creditos
{
    public static bool Open { get; private set; }
    // true cuando se cerraron (por terminar o por saltarlos): la pantalla de victoria lo usa.
    public static bool Finished { get; private set; }
    static float start, speed = 42f, contentHeight = -1f;
    static string[] lines;

    public static void Show()
    {
        Open = true;
        Finished = false;
        start = Time.unscaledTime;
        lines = Load();
        contentHeight = -1f;
        // Velocidad para que los créditos duren lo mismo que la canción.
        float song = GameAudio.StartCreditsMusic();
        speed = 42f;
        songLength = song;
    }
    static float songLength;

    public static void Close()
    {
        if (!Open) return;
        Open = false;
        Finished = true;
        GameAudio.StopCreditsMusic();
    }

    static string[] Load()
    {
        try
        {
            var path = System.IO.Path.Combine(Application.streamingAssetsPath, "creditos.txt");
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllLines(path);
        }
        catch { }
        return new[] { "LEYENDAS DEL PERÚ", "", "# GRACIAS POR JUGAR" };
    }

    public static void Draw(float w)
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Space)) { Close(); e.Use(); return; }
        if (e.type == EventType.MouseDown) { Close(); e.Use(); return; }

        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, w, 720), Texture2D.whiteTexture);
        var bg = UIStyle.Tex("fondo_clases");
        if (bg != null)
        {
            GUI.color = new Color(1, 1, 1, 0.35f);
            Bestiario.Cover(new Rect(0, 0, w, 720), bg);
        }
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(0, 0, w, 720), UIStyle.Tex("vineta"));

        // Ajusta la velocidad (una vez) para que el último renglón salga justo cuando acaba la canción.
        if (contentHeight > 0f && songLength > 10f) speed = Mathf.Clamp((contentHeight + 600f) / (songLength - 3f), 25f, 60f);
        float y = 560 - (Time.unscaledTime - start) * speed;
        float y0 = y;
        var title = new GUIStyle(UIStyle.Title) { fontSize = 54, alignment = TextAnchor.MiddleCenter };
        var head = new GUIStyle(UIStyle.Title) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
        var body = new GUIStyle(UIStyle.Text) { fontSize = 21, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i].Trim();
            if (i == 0) { Fade(y); GUI.Label(new Rect(0, y, w, 70), l, title); y += 90; continue; }
            if (l.StartsWith("#"))
            {
                y += 26;
                Fade(y);
                GUI.Label(new Rect(0, y, w, 36), l.TrimStart('#', ' '), head);
                UIStyle.Separator(new Rect(w / 2 - 140, y + 34, 280, 16));
                y += 60;
            }
            else
            {
                Fade(y);
                if (l.Length > 0) GUI.Label(new Rect(w / 2 - 450, y, 900, 30), l, body);
                y += l.Length > 0 ? 32 : 16;
            }
        }
        if (contentHeight < 0f) contentHeight = y - y0;
        GUI.color = Color.white;
        // Al terminar se cierran solos.
        if (y < -40f) Close();
        GUI.Label(new Rect(w - 300, 14, 270, 22), "Clic, Esc o Espacio para saltar", new GUIStyle(UIStyle.Small) { fontSize = 12, alignment = TextAnchor.MiddleRight });
        UIStyle.ScreenCorners(w, 80);
    }

    // Aparece al entrar por abajo y se desvanece al salir por arriba.
    static void Fade(float y) => GUI.color = new Color(1, 1, 1, Mathf.Clamp01(Mathf.Min((720f - y) / 80f, (y + 40f) / 120f)));
}
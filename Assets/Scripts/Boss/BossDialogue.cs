using System.Collections.Generic;
using UnityEngine;

// Diálogos de los jefes en batalla: su voz (Resources/Audio/dlg_<jefe>_<momento>.wav) y un subtítulo.
// Las frases están en Resources/dialogos.txt (jefe|momento|frase). Solo visual/sonido: cada PC lo reproduce.
public class BossDialogue : MonoBehaviour
{
    static BossDialogue instance;
    static Dictionary<string, string> lines;

    AudioSource voice;
    string subtitle, speaker;
    float subtitleUntil, lastLineAt = -100f;

    static BossDialogue Get()
    {
        if (instance == null)
        {
            var go = new GameObject("Dialogos jefe");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BossDialogue>();
            instance.voice = go.AddComponent<AudioSource>();
        }
        return instance;
    }

    static void Load()
    {
        if (lines != null) return;
        lines = new Dictionary<string, string>();
        var txt = Resources.Load<TextAsset>("dialogos");
        if (txt == null) return;
        foreach (var raw in txt.text.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            var p = l.Split('|');
            if (p.Length >= 3) lines[p[0] + "_" + p[1]] = p[2];
        }
    }

    public static bool IsSpeaking => instance != null && instance.voice.isPlaying;

    // important = intro, fase 2 y muerte: interrumpen lo que esté diciendo y no esperan.
    public static void Say(BossAI boss, string moment, bool important = false)
    {
        if (boss == null || GameAudio.Hushed) return;
        Load();
        var d = Get();
        if (!important && (d.voice.isPlaying || Time.time - d.lastLineAt < 7f)) return;
        string key = boss.SfxPrefix + "_" + moment;
        if (!lines.TryGetValue(key, out var text)) return;
        d.lastLineAt = Time.time;
        var clip = GameAudio.Clip("dlg_" + key);
        float length = 3f;
        if (clip != null)
        {
            d.voice.Stop();
            d.voice.clip = clip;
            d.voice.volume = GameAudio.Instance != null ? Mathf.Clamp01(GameAudio.Instance.narratorVolume * 1.05f) : 0.9f;
            d.voice.Play();
            length = clip.length;
        }
        d.subtitle = text;
        d.speaker = boss.Data.bossName;
        d.subtitleUntil = Time.time + Mathf.Max(2.5f, length + 0.6f);
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(subtitle) || Time.time > subtitleUntil || LoadingScreen.Showing || PauseMenu.Open) return;
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float w = Screen.width / scale;
        float a = Mathf.Clamp01((subtitleUntil - Time.time) * 2f);   // se desvanece al final
        var text = new GUIStyle(UIStyle.Text) { fontSize = 16, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
        string line = $"<color=#f0b060><b>{speaker}:</b></color> «{subtitle}»";
        float h = text.CalcHeight(new GUIContent(line), 560f) + 10f;
        var r = new Rect(w / 2 - 300, 134, 600, h);   // arriba, bajo la barra del jefe: no tapa la pelea
        GUI.color = new Color(0, 0, 0, 0.4f * a);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(1, 1, 1, a);
        GUI.Label(new Rect(r.x + 20, r.y, r.width - 40, r.height), line, text);
        GUI.color = Color.white;
    }
}

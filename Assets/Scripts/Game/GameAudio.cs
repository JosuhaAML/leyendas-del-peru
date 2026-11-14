using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Música por escena y efectos de sonido. Los audios están en Assets/Resources/Audio
// (compuestos por código con Herramientas/generar_audio.py). Vive junto al NetworkManager.
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Range(0f, 1f)] public float musicVolume = 0.35f;
    [Range(0f, 1f)] public float effectsVolume = 0.8f;
    [Range(0f, 1f)] public float narratorVolume = 0.9f;
    float savedMusic, savedEffects, savedNarrator;

    // Volumen relativo de cada efecto (algunos salieron más fuertes que otros).
    static readonly Dictionary<string, float> Gain = new Dictionary<string, float>
    {
        { "magia", 0.45f }, { "rugido", 0.8f }, { "muerte_jefe", 0.9f }, { "area", 0.9f },
    };

    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    AudioSource music, effects;

    void Awake()
    {
        Instance = this;
        // Volumen guardado en esta PC (se cambia en el menú principal o en la pausa).
        musicVolume = savedMusic = PlayerPrefs.GetFloat("VolMusica", musicVolume);
        effectsVolume = savedEffects = PlayerPrefs.GetFloat("VolEfectos", effectsVolume);
        narratorVolume = savedNarrator = PlayerPrefs.GetFloat("VolNarrador", narratorVolume);
        music = gameObject.AddComponent<AudioSource>();
        music.loop = true;
        music.volume = musicVolume;
        effects = gameObject.AddComponent<AudioSource>();
        foreach (var clip in Resources.LoadAll<AudioClip>("Audio")) clips[clip.name] = clip;
        Debug.Log($"AUDIO cargados: {clips.Count} ({string.Join(", ", clips.Keys)})");
        SceneManager.activeSceneChanged += (_, scene) => PlayMusicFor(scene.name);
        PlayMusicFor(SceneManager.GetActiveScene().name);
    }

    // Cada jefe tiene su música (y una versión intensa para su segunda fase) y cada nivel su sonido ambiente.
    static (string music, string ambient) TracksFor(string scene)
    {
        if (scene.Contains("Chullachaqui")) return ("m_chullachaqui", "amb_lluvia");
        if (scene.Contains("Yacumama")) return ("m_yacumama", "amb_lluvia");
        if (scene.Contains("Bufeo")) return ("m_bufeo", "amb_rio");
        if (scene.Contains("Tunche")) return ("m_tunche", "amb_noche");
        if (scene.Contains("Pishtaco")) return ("m_pishtaco", "amb_viento");
        return ("andina", null);
    }

    AudioSource ambient;
    string baseTrack;
    bool intense;
    float fade = 1f;       // 1 = volumen normal; baja a 0 para cambiar de pista
    string pendingTrack;
    BossAI roaredFor;

    void PlayMusicFor(string scene)
    {
        if (ambient == null)
        {
            ambient = gameObject.AddComponent<AudioSource>();
            ambient.loop = true;
        }
        if (scene == "Victoria")
        {
            music.Stop();
            ambient.Stop();
            Play("victoria");
            return;
        }
        var (track, amb) = TracksFor(scene);
        baseTrack = track;
        intense = false;
        SetTrack(track, immediate: true);
        if (amb != null && clips.TryGetValue(amb, out var a)) { if (ambient.clip != a) { ambient.clip = a; ambient.Play(); } }
        else ambient.Stop();
    }

    void SetTrack(string track, bool immediate)
    {
        if (!clips.TryGetValue(track, out var clip) || music.clip == clip) return;
        if (immediate) { music.clip = clip; music.Play(); fade = 1f; pendingTrack = null; }
        else pendingTrack = track;
    }

    void Update()
    {
        // Guarda el volumen cuando cambia.
        if (musicVolume != savedMusic || effectsVolume != savedEffects || narratorVolume != savedNarrator)
        {
            savedMusic = musicVolume; savedEffects = effectsVolume; savedNarrator = narratorVolume;
            PlayerPrefs.SetFloat("VolMusica", musicVolume);
            PlayerPrefs.SetFloat("VolEfectos", effectsVolume);
            PlayerPrefs.SetFloat("VolNarrador", narratorVolume);
            PlayerPrefs.Save();
        }
        if (narration != null) narration.volume = narratorVolume;
        // Segunda fase: cambia a la versión intensa con un fundido corto.
        var boss = BossAI.Active;
        if (!intense && boss != null && boss.Phase2 && baseTrack != null && baseTrack.StartsWith("m_"))
        {
            intense = true;
            SetTrack(baseTrack + "_intensa", immediate: false);
        }
        // (El rugido del jefe al aparecer ahora lo hace su entrada, cuando ya terminó la narración.)

        if (pendingTrack != null)
        {
            fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime);
            if (fade <= 0f)
            {
                var next = pendingTrack;
                pendingTrack = null;
                if (clips.TryGetValue(next, out var clip)) { music.clip = clip; music.Play(); }
            }
        }
        else fade = Mathf.MoveTowards(fade, 1f, Time.deltaTime);
        // Pantalla de carga: silencio total para escuchar al narrador. Después, mientras sigue hablando,
        // la música entra muy suave y sube cuando termina (fundido de ~2 s, se siente como una película).
        float target = Hushed || CreditsPlaying ? 0f : Narrating ? 0.18f : BossDialogue.IsSpeaking ? 0.5f : 1f;   // baja también cuando habla un jefe
        duck = Mathf.MoveTowards(duck, target, Time.unscaledDeltaTime * (target < duck ? 2f : 0.5f));
        music.volume = musicVolume * fade * duck;   // el menú de pausa cambia el volumen en vivo
        if (ambient != null) ambient.volume = Mathf.Clamp01(musicVolume * 1.4f) * Mathf.Max(duck, Hushed ? 0f : 0.45f);
    }

    float duck = 1f;

    // Mientras se muestra la pantalla de carga no suena nada más que el narrador.
    public static bool Hushed => LoadingScreen.Showing;
    // ---- Canción de los créditos (sustituye a la música del juego mientras suenan)
    AudioSource credits;
    public static bool CreditsPlaying => Instance != null && Instance.credits != null && Instance.credits.isPlaying && Instance.credits.volume > 0.01f;

    // Devuelve cuánto dura la canción (0 si no hay).
    public static float StartCreditsMusic()
    {
        if (Instance == null || !Instance.clips.TryGetValue("creditos", out var clip)) { Play("victoria"); return 0f; }
        if (Instance.credits == null) Instance.credits = Instance.gameObject.AddComponent<AudioSource>();
        Instance.StopAllCoroutines();
        Instance.credits.clip = clip;
        Instance.credits.loop = false;
        Instance.credits.volume = Mathf.Clamp01(Instance.musicVolume * 2f);
        Instance.credits.Play();
        return clip.length;
    }

    public static void StopCreditsMusic()
    {
        if (Instance == null || Instance.credits == null || !Instance.credits.isPlaying) return;
        Instance.StartCoroutine(Instance.FadeCredits());
    }

    System.Collections.IEnumerator FadeCredits()
    {
        float v0 = credits.volume;
        for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime) { credits.volume = Mathf.Lerp(v0, 0f, t / 1.2f); yield return null; }
        credits.Stop();
    }

    // Narración de la leyenda (voz): suena aparte y baja la música mientras habla.
    AudioSource narration;

    public static void Narrate(string name)
    {
        if (Instance == null || !Instance.clips.TryGetValue(name, out var clip)) return;
        if (Instance.narration == null) Instance.narration = Instance.gameObject.AddComponent<AudioSource>();
        Instance.narration.Stop();
        Instance.narration.clip = clip;
        Instance.narration.volume = Instance.narratorVolume;
        Instance.narration.Play();
    }

    public static bool Narrating => Instance != null && Instance.narration != null && Instance.narration.isPlaying;

    public static void Play(string name)
    {
        if (Instance == null || Hushed || !Instance.clips.TryGetValue(name, out var clip)) return;
        Gain.TryGetValue(name, out float g);
        // Mientras el narrador sigue hablando, los efectos suenan más bajo para no taparlo.
        Instance.effects.PlayOneShot(clip, Instance.effectsVolume * (g > 0f ? g : 1f) * (Narrating ? 0.4f : 1f));
    }

    public static bool Has(string name) => Instance != null && Instance.clips.ContainsKey(name);
    public static AudioClip Clip(string name) => Instance != null && Instance.clips.TryGetValue(name, out var c) ? c : null;

    // Sonido en un punto del mundo (las voces de los jefes): se oye más fuerte cerca y hacia el lado donde está.
    public static void PlayAt(string name, Vector3 pos, float volume = 1f, float pitchJitter = 0.06f)
    {
        if (Instance == null || Hushed || !Instance.clips.TryGetValue(name, out var clip)) return;
        var go = new GameObject("Sonido " + name);
        go.transform.position = pos;
        var s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.spatialBlend = 0.75f;               // en parte 3D: se ubica, pero nunca desaparece del todo
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 10f;
        s.maxDistance = 70f;
        s.dopplerLevel = 0f;
        s.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        s.volume = Mathf.Clamp01(Instance.effectsVolume * volume * (Narrating ? 0.4f : 1f));
        s.Play();
        Destroy(go, clip.length / Mathf.Max(0.5f, s.pitch) + 0.2f);
    }
}

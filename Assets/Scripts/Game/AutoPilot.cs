using Unity.Netcode;
using UnityEngine;

// Piloto automático para probar partidas de varios jugadores en una sola PC sin gente.
// Se activa abriendo el juego con:  -autojugar -unirse 127.0.0.1 -rol 2 -nombre "Bot Ana"
//  · se une solo a la partida (red local) con su clase y su nombre,
//  · en batalla se acerca al jefe a su distancia ideal, rodea, usa habilidades y el ataque básico,
//  · esquiva cuando hay una runa de peligro cerca o al azar, y revive aliados caídos si puede.
public class AutoPilot : MonoBehaviour
{
    public static bool Active { get; private set; }
    static float strafeSign = 1f, nextStrafeFlip, nextDodgeRoll, nextBasic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = System.Environment.GetCommandLineArgs();
        if (System.Array.IndexOf(args, "-autojugar") < 0) return;
        Active = true;
        int r = System.Array.IndexOf(args, "-rol");
        if (r >= 0 && r + 1 < args.Length) PlayerAbilities.SessionRole = int.Parse(args[r + 1]);
        int n = System.Array.IndexOf(args, "-nombre");
        if (n >= 0 && n + 1 < args.Length) PlayerAbilities.SessionName = args[n + 1];
        var go = new GameObject("Piloto automatico");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoPilot>();
        strafeSign = Random.value < 0.5f ? 1f : -1f;
    }

    float joinAt = 3f;

    // -puerto N: usar otro puerto (pruebas mientras hay otra partida abierta en la PC).
    public static ushort TestPort()
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-puerto");
        return i >= 0 && i + 1 < a.Length && ushort.TryParse(a[i + 1], out ushort p) ? p : NetworkUI.LanPort;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        // Unirse solo a la partida local.
        if (!nm.IsClient && !nm.IsServer && Time.time > joinAt && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-unirse") >= 0)
        {
            joinAt = Time.time + 6f;   // reintenta si el anfitrión aún no abrió
            var args = System.Environment.GetCommandLineArgs();
            int u = System.Array.IndexOf(args, "-unirse");
            string ip = u >= 0 && u + 1 < args.Length ? args[u + 1] : "127.0.0.1";
            nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData(ip, TestPort());
            nm.StartClient();
            return;
        }
        var me = nm.LocalClient?.PlayerObject != null ? nm.LocalClient.PlayerObject.GetComponent<PlayerAbilities>() : null;
        var boss = BossAI.Active;
        if (me == null || boss == null || me.Abilities == null || HUD.IntroShowing || LoadingScreen.Showing) return;
        if (me.GetComponent<Health>().IsDead || boss.GetComponent<Health>().IsDead) return;

        // Habilidades: en orden de la más fuerte a la más débil, si está lista y en rango.
        float dist = Vector3.Distance(me.transform.position, boss.transform.position) - boss.HitRadius;
        for (int i = me.Abilities.Length - 1; i >= 0; i--)
        {
            var a = me.Abilities[i];
            if (Time.time < a.readyAt || !PlayerAbilities.IsUnlocked(a)) continue;
            if (a.damage > 0f && dist > a.range) continue;
            if (a.damage < 0f && Random.value > 0.02f) continue;   // curaciones: de vez en cuando
            a.readyAt = Time.time + a.cooldown;
            me.TestUse(i);
            return;
        }
        if (me.UltReady && Random.value < 0.01f) me.TryUlt();
        if (Time.time > nextBasic && dist <= me.Basic.range) { nextBasic = Time.time + me.Basic.cooldown + 0.1f; me.TestBasic(); }
    }

    // Dirección de movimiento para PlayerController (y si conviene esquivar ahora).
    public static Vector3 Steer(PlayerController pc, out bool dodge)
    {
        dodge = false;
        var boss = BossAI.Active;
        var me = pc.GetComponent<PlayerAbilities>();
        if (boss == null || me == null || me.Abilities == null || HUD.IntroShowing) return Vector3.zero;
        var to = boss.transform.position - pc.transform.position; to.y = 0f;
        float dist = to.magnitude - boss.HitRadius;
        bool melee = me.Role == PlayerRole.Guerrero || me.Role == PlayerRole.Guardian;
        float ideal = melee ? 1.8f : 11f;
        if (Time.time > nextStrafeFlip) { nextStrafeFlip = Time.time + Random.Range(2f, 5f); strafeSign = -strafeSign; }
        var side = Vector3.Cross(Vector3.up, to.normalized) * strafeSign;
        Vector3 move = dist > ideal + 1f ? to.normalized : dist < ideal - 2f ? -to.normalized : side * 0.6f;
        // Esquiva: si el jefe está atacando de cerca o al azar cada tanto.
        if (Time.time > nextDodgeRoll)
        {
            nextDodgeRoll = Time.time + Random.Range(1.5f, 3.5f);
            dodge = (melee && dist < 3f && Random.value < 0.45f) || Random.value < 0.15f;
        }
        return move.normalized;
    }
}

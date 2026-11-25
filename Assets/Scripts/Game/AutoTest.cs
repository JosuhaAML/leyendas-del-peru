using System.Collections;
using System.IO;
using Unity.Netcode;
using UnityEngine;

// Prueba automática: abre el juego con "-autotest <carpeta>", crea una partida local,
// entra al nivel 1 y guarda capturas del menú, la presentación del jefe y el combate.
public class AutoTest : MonoBehaviour
{
    string outDir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-autotest");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("AutoTest");
        DontDestroyOnLoad(go);
        var test = go.AddComponent<AutoTest>();
        test.outDir = args[i + 1];
        int n = System.Array.IndexOf(args, "-nivel");
        if (n >= 0 && n + 1 < args.Length) test.level = int.Parse(args[n + 1]) - 1;
        int r = System.Array.IndexOf(args, "-rol");
        if (r >= 0 && r + 1 < args.Length) PlayerAbilities.PreferredRole = int.Parse(args[r + 1]);
    }

    int level;

    IEnumerator Start()
    {
        Directory.CreateDirectory(outDir);
        var args = System.Environment.GetCommandLineArgs();
        if (System.Array.IndexOf(args, "-cliente") >= 0)
        {
            // Segundo jugador de prueba: se une a la partida local y se queda quieto.
            yield return new WaitForSeconds(2f);
            NetworkManager.Singleton.StartClient();
            yield return new WaitForSeconds(70f);
            Application.Quit();
            yield break;
        }

        // -progreso N: simula que ya venciste N niveles (para ver el camino con candados y chakanas).
        int pg = System.Array.IndexOf(args, "-progreso");
        if (pg >= 0 && pg + 1 < args.Length)
        {
            Progress.Reset();
            for (int k = 0; k < int.Parse(args[pg + 1]); k++) Progress.MarkBeaten(k);
        }

        yield return new WaitForSeconds(3f);
        Shot("1_menu");
        bool resol = System.Array.IndexOf(args, "-resoltest") >= 0;
        if (resol)
        {
            // En el menú la calidad elegida NO debe bajar la resolución (siempre la del monitor).
            Graficos.Cambiar(CalidadGrafica.Baja);
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log($"AUTOTEST resol menú con Baja: {Screen.width}x{Screen.height} (objetivo {ScreenMode.Objetivo()}) efectiva={Graficos.Efectiva}");
        }
        int hostArg = System.Array.IndexOf(args, "-onlinehost"), joinArg = System.Array.IndexOf(args, "-onlinejoin");
        if (hostArg >= 0 || joinArg >= 0)
        {
            // Prueba real del modo en línea (Unity Relay): una PC crea la partida y deja el código en un archivo,
            // la otra lo lee y se une.
            var ui = FindFirstObjectByType<NetworkUI>();
            if (hostArg >= 0)
            {
                string file = args[hostArg + 1];
                ui.TestHostOnline();
                for (float t = 0; t < 30f && string.IsNullOrEmpty(ui.SessionCode); t += 0.5f) yield return new WaitForSeconds(0.5f);
                Debug.Log($"AUTOTEST online anfitrión: código={ui.SessionCode} estado={ui.Status}");
                System.IO.File.WriteAllText(file, ui.SessionCode ?? "");
                Shot("o1_anfitrion");
                for (float t = 0; t < 45f; t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    if (NetworkManager.Singleton.ConnectedClientsList.Count >= 2) break;
                }
                Debug.Log($"AUTOTEST online jugadores conectados: {NetworkManager.Singleton.ConnectedClientsList.Count}");
                yield return new WaitForSeconds(2f);
                Shot("o2_anfitrion_con_amigo");
                yield return new WaitForSeconds(4f);
            }
            else
            {
                string file = args[joinArg + 1];
                for (float t = 0; t < 40f && (!System.IO.File.Exists(file) || System.IO.File.ReadAllText(file).Trim().Length == 0); t += 0.5f) yield return new WaitForSeconds(0.5f);
                string code = System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Trim() : "";
                Debug.Log("AUTOTEST online uniéndose con código " + code);
                ui.TestJoinOnline(code);
                for (float t = 0; t < 25f && !NetworkManager.Singleton.IsConnectedClient; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Debug.Log($"AUTOTEST online cliente conectado={NetworkManager.Singleton.IsConnectedClient} estado={ui.Status}");
                yield return new WaitForSeconds(3f);
                Shot("o3_cliente");
                // Clases únicas: ¿qué clase tengo, quién tiene cada una, y qué pasa si intento tomar una ocupada?
                var me = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerAbilities>();
                foreach (var pa in FindObjectsByType<PlayerAbilities>())
                    Debug.Log($"AUTOTEST clases: jugador {pa.OwnerClientId} {(pa.IsOwner ? "(yo)" : "")} = {pa.Role}");
                Debug.Log($"AUTOTEST aviso de clase: {PlayerAbilities.RoleNotice}");
                bool ok = me.ChooseRole(0);
                yield return new WaitForSeconds(1f);
                Debug.Log($"AUTOTEST intento tomar Guerrero (ocupado): aceptado={ok}, sigo siendo {me.Role}");
                NetworkUI.ShowClasses = true;
                yield return new WaitForSeconds(1.5f);
                Shot("o4_pantalla_clases");
                NetworkUI.ShowClasses = false;
                yield return new WaitForSeconds(4f);
            }
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-bestiariotest") >= 0)
        {
            Bestiario.Open = true;
            for (int i = 0; i < 5; i += 2)
            {
                Bestiario.Select(i);
                yield return new WaitForSeconds(0.6f);
                Shot($"b{i + 1}");
            }
            Bestiario.Open = false;
            Creditos.Show();
            yield return new WaitForSecondsRealtime(4f);
            Shot("c1_creditos");
            yield return new WaitForSecondsRealtime(6f);
            Shot("c2_creditos");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-animtest") >= 0)
        {
            // Prueba de animaciones: cada héroe corriendo, atacando y lanzando hechizo (en la pantalla de clases).
            NetworkUI.ShowClasses = true;
            HeroPreview.TestNoAuto = true;
            for (int r = 0; r < 5; r++)
            {
                PlayerAbilities.PreferredRole = r;
                yield return new WaitForSeconds(0.8f);
                var pv = HeroPreview.Get();
                pv.Yaw = 70f; pv.LastDragAt = Time.time + 100f;   // de lado para ver las piernas
                HeroPreview.TestRun = 6f;
                yield return new WaitForSeconds(0.9f);
                Shot($"anim_{r}_a_correr");
                yield return new WaitForSeconds(0.13f);
                Shot($"anim_{r}_b_correr");
                HeroPreview.TestRun = 0f;
                pv.Yaw = -20f;
                yield return new WaitForSeconds(0.6f);
                pv.TestAction("Attack");
                yield return new WaitForSeconds(0.14f);
                Shot($"anim_{r}_c_ataque_carga");
                yield return new WaitForSeconds(0.16f);
                Shot($"anim_{r}_d_ataque_golpe");
                yield return new WaitForSeconds(0.8f);
                pv.TestAction("Cast");
                yield return new WaitForSeconds(0.45f);
                Shot($"anim_{r}_e_hechizo");
                yield return new WaitForSeconds(0.6f);
                pv.TestAction("Wave");
                yield return new WaitForSeconds(0.9f);
                Shot($"anim_{r}_f_saludo");
                yield return new WaitForSeconds(1.8f);
                pv.TestAction("Dance");
                yield return new WaitForSeconds(1.2f);
                Shot($"anim_{r}_g_baile");
                yield return new WaitForSeconds(3.5f);
            }
            Application.Quit();
            yield break;
        }

        NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData("127.0.0.1", AutoPilot.TestPort(), "0.0.0.0");
        NetworkManager.Singleton.StartHost();
        yield return new WaitForSeconds(0.5f);
        if (System.Array.IndexOf(args, "-sonidotest") >= 0) FindFirstObjectByType<NetworkUI>().TestSound(true);
        yield return new WaitForSeconds(0.3f);
        Shot("1b_menu_anfitrion");
        if (System.Array.IndexOf(args, "-sonidotest") >= 0)
        {
            var me0 = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerAbilities>();
            Debug.Log($"AUTOTEST eligió clase={me0.HasChosen.Value}");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
            yield break;
        }
        NetworkUI.ShowClasses = true;
        yield return new WaitForSeconds(1.2f);
        Shot("1c_clases");
        // Gira el héroe 3D para comprobar la vista de 360°.
        var preview = HeroPreview.Get();
        preview.Yaw += 120f;
        preview.LastDragAt = Time.time;
        yield return new WaitForSeconds(0.5f);
        Shot("1d_clases_girado");
        yield return new WaitForSeconds(0.3f);   // la captura se toma al final del cuadro
        NetworkUI.ShowClasses = false;
        if (System.Array.IndexOf(args, "-esperar") >= 0)
            for (float t = 0; t < 40f && NetworkManager.Singleton.ConnectedClientsList.Count < WaitPlayers(args); t += 0.5f)
                yield return new WaitForSeconds(0.5f);
        Debug.Log("AUTOTEST jugadores: " + NetworkManager.Singleton.ConnectedClientsList.Count);
        yield return new WaitForSeconds(1.5f);
        foreach (var pa in FindObjectsByType<PlayerAbilities>())
            Debug.Log($"AUTOTEST clase del jugador {pa.OwnerClientId}: {PlayerAbilities.RoleName(pa.Role)}");
        yield return new WaitForSeconds(1f);
        LevelManager.Instance.StartAt(level);

        yield return new WaitForSeconds(1.2f);
        Shot("2a_carga");
        while (LoadingScreen.Showing) yield return null;
        yield return new WaitForSeconds(1.8f);
        Shot("2_presentacion");
        Debug.Log("AUTOTEST jefe activo: " + (BossAI.Active != null ? BossAI.Active.Data.bossName : "ninguno"));
        if (resol)
        {
            // Dentro del nivel: Baja = 75 %, Media y Alta = 100 %; repetir varias veces no debe achicarla más.
            CalidadGrafica[] orden = { CalidadGrafica.Baja, CalidadGrafica.Alta, CalidadGrafica.Media, CalidadGrafica.Baja, CalidadGrafica.Alta,
                                       CalidadGrafica.Baja, CalidadGrafica.Media, CalidadGrafica.Baja, CalidadGrafica.Alta };
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log($"AUTOTEST resol nivel al entrar: {Screen.width}x{Screen.height} efectiva={Graficos.Efectiva}");
            foreach (var q in orden)
            {
                Graficos.Cambiar(q);
                yield return new WaitForSecondsRealtime(1.5f);
                Debug.Log($"AUTOTEST resol nivel {q}: {Screen.width}x{Screen.height} (objetivo {ScreenMode.Objetivo()}) sombras={QualitySettings.shadows}");
            }
            Application.Quit();
            yield break;
        }        if (System.Array.IndexOf(args, "-pausatest") >= 0)
        {
            // Menú de pausa con el selector de gráficos.
            yield return new WaitForSeconds(3f);
            PauseMenu.SetOpen(true);
            yield return new WaitForSeconds(0.5f);
            Shot("pausa");
            Application.Quit();
            yield break;
        }

        var player = NetworkManager.Singleton.LocalClient.PlayerObject;

        if (System.Array.IndexOf(args, "-entradatest") >= 0)
        {
            // Entrada del jefe tras el retrato.
            while (!BossEntrance.Playing) yield return null;
            float[] waits = { 0.5f, 0.8f, 0.9f, 0.6f, 0.9f };
            for (int i = 0; i < waits.Length; i++)
            {
                yield return new WaitForSeconds(waits[i]);
                Shot($"e{i + 1}");
            }
            Application.Quit();
            yield break;
        }
        // El resto de pruebas empieza cuando el jefe ya terminó su entrada.
        while (HUD.IntroShowing) yield return null;

        if (System.Array.IndexOf(args, "-ataquetest") >= 0)
        {
            // Los ataques nuevos del jefe con su runa de aviso: barrido, rugido, salto y ataque en área.
            yield return new WaitForSeconds(6f);
            var boss = BossAI.Active;
            boss.Paused = true;
            player.GetComponent<Health>().Invulnerable = true;
            var cc4 = player.GetComponent<CharacterController>();
            cc4.enabled = false;
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(
                boss.transform.position + boss.transform.forward * (boss.HitRadius + 6f), Quaternion.LookRotation(-boss.transform.forward), Vector3.one);
            cc4.enabled = true;
            yield return new WaitForSeconds(0.5f);
            foreach (int v in boss.IsFinalBoss ? new[] { 11, 12, 13, 14, 15 } : boss.GetComponentInChildren<SerpentMotion>() != null && GameObject.Find("Isla") != null ? new[] { 16, 17, 18, 19 } : boss.Data.bossName.Contains("Chulla") ? new[] { 20, 21 } : boss.Data.bossName.Contains("Bufeo") ? new[] { 22, 23 } : boss.Data.bossName.Contains("Tunche") ? new[] { 24, 25 } : new[] { 6, 3, 4, 5 })
            {
                boss.TestAttack(v);
                yield return new WaitForSeconds(v == 6 ? 1.2f : 0.6f);
                Shot($"a{v}_aviso");
                yield return new WaitForSeconds(v == 5 ? 0.85f : 0.65f);
                Shot($"a{v}_golpe");
                yield return new WaitForSeconds(v == 12 ? 6.5f : v == 13 || v == 15 || v == 22 || v == 24 ? 4f : 2f);
            }
            if (boss.IsFinalBoss)
            {
                var bhF = boss.GetComponent<Health>();
                bhF.TakeDamage(bhF.Current.Value - bhF.Max * 0.14f, 0);
                yield return new WaitForSeconds(1.2f);
                Shot("a_furia_final");
                yield return new WaitForSeconds(1.5f);
                Shot("a_furia_pilares");
            }
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-observar") >= 0)
        {
            // Partida de varios jugadores (piloto automático): cuánto dura la pelea y cuántos caen.
            var bh = BossAI.Active.GetComponent<Health>();
            float start = Time.time; int deaths = 0; var wasDead = new System.Collections.Generic.HashSet<ulong>();
            Debug.Log($"AUTOTEST observar: jugadores {NetworkManager.Singleton.ConnectedClientsList.Count}, vida del jefe {bh.Max:0}");
            int shot = 0;
            int si = System.Array.IndexOf(args, "-segundos");
            float limit = si >= 0 && si + 1 < args.Length ? float.Parse(args[si + 1]) : 240f;
            while (!bh.IsDead && Time.time - start < limit)
            {
                yield return new WaitForSeconds(1f);
                foreach (var c2 in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (c2.PlayerObject == null) continue;
                    bool dead = c2.PlayerObject.GetComponent<Health>().IsDead;
                    if (dead && wasDead.Add(c2.ClientId)) deaths++;
                    if (!dead) wasDead.Remove(c2.ClientId);
                }
                if ((int)(Time.time - start) % 30 == 0 && shot < 4) Shot($"v{++shot}");
            }
            foreach (var kv in PlayerAbilities.DebugDamage) Debug.Log($"AUTOTEST daño {kv.Key}: {kv.Value:0}");
            Debug.Log($"AUTOTEST resultado: {(bh.IsDead ? "jefe vencido" : "jefe vivo")} en {Time.time - start:0} s, vida restante {bh.Current.Value:0}, caídas de jugadores {deaths}, DAÑO POR SEGUNDO {(bh.Max - bh.Current.Value) / (Time.time - start):0.0}");
            Application.Quit();
            yield break;
        }
        if (System.Array.IndexOf(args, "-campana") >= 0)
        {
            // Aventura completa con piloto automático: cada nivel hasta vencer al jefe, luego el siguiente, y al final los créditos.
            for (int lvl = level; lvl < LevelManager.Instance.levelScenes.Length; lvl++)
            {
                float t0 = Time.time, nextShot = Time.time + 20f;
                int shot = 0;
                while (true)
                {
                    var b = BossAI.Active;
                    if (b != null && b.GetComponent<Health>().IsDead) break;
                    if (Time.time - t0 > 480f) { Debug.Log($"AUTOTEST campaña: nivel {lvl + 1} sin terminar a los 8 min"); break; }
                    if (Time.time >= nextShot) { nextShot = Time.time + 30f; Shot($"n{lvl + 1}_{++shot:00}"); }
                    // -matarjefe S: tras S segundos de pelea se le baja toda la vida (para ver el final rápido).
                    int mk = System.Array.IndexOf(args, "-matarjefe");
                    if (mk >= 0 && b != null && Time.time - t0 > float.Parse(args[mk + 1]))
                        b.GetComponent<Health>().TakeDamage(999999f, 0);
                    yield return new WaitForSeconds(0.5f);
                }
                var boss = BossAI.Active;
                Debug.Log($"AUTOTEST campaña: nivel {lvl + 1} ({(boss != null ? boss.Data.bossName : "?")}) vencido en {Time.time - t0:0} s");
                yield return new WaitForSeconds(6.5f);
                Shot($"n{lvl + 1}_victoria");
                yield return new WaitForSeconds(2f);
                var old = BossAI.Active;
                LevelManager.Instance.NextLevel();
                if (lvl == LevelManager.Instance.levelScenes.Length - 1) break;
                // Espera a que cargue el siguiente nivel y termine la presentación del jefe.
                float w0 = Time.time;
                while ((BossAI.Active == null || BossAI.Active == old || LoadingScreen.Showing || HUD.IntroShowing) && Time.time - w0 < 60f)
                    yield return new WaitForSeconds(0.5f);
            }
            // Epílogo (dos frases del narrador), luego créditos (salen solos) y pantalla de victoria.
            yield return new WaitForSecondsRealtime(3f);
            Shot("fin_epilogo_1");
            Debug.Log($"AUTOTEST campaña: epílogo abierto={Epilogo.Open}");
            yield return new WaitForSecondsRealtime(6f);
            Shot("fin_epilogo_2");
            float e0 = Time.realtimeSinceStartup;
            while (Epilogo.Open && Time.realtimeSinceStartup - e0 < 30f) yield return null;
            Debug.Log($"AUTOTEST campaña: epílogo terminó en {Time.realtimeSinceStartup - e0 + 9f:0} s");
            yield return new WaitForSecondsRealtime(4f);
            Shot("fin_creditos_1");
            yield return new WaitForSecondsRealtime(30f);
            Shot("fin_creditos_2");
            Debug.Log($"AUTOTEST campaña: créditos abiertos={Creditos.Open} canción={GameAudio.CreditsPlaying}");
            float c0 = Time.realtimeSinceStartup;
            while (Creditos.Open && Time.realtimeSinceStartup - c0 < 70f) yield return null;
            yield return new WaitForSeconds(2f);
            Shot("fin_victoria");
            Debug.Log("AUTOTEST campaña: terminada");
            yield return new WaitForSeconds(25f);
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-atascotest") >= 0)
        {
            // Mete al jugador dentro de una roca y comprueba que el anti-atascos lo saque solo.
            yield return new WaitForSeconds(1f);
            BossAI.Active.Paused = true;
            player.GetComponent<Health>().Invulnerable = true;
            Transform rock = null;
            // Una roca real de Poly Haven (malla hueca), la más grande: es donde uno puede quedar encerrado.
            foreach (var mc in FindObjectsByType<MeshCollider>())
                if ((mc.name.Contains("rock") || mc.name.Contains("boulder") || mc.transform.root.name.Contains("rock") || mc.transform.root.name.Contains("boulder")) && (rock == null || mc.bounds.size.x > rock.GetComponent<Collider>().bounds.size.x)) rock = mc.transform;
            if (rock != null) Debug.Log("AUTOTEST atasco: roca " + rock.root.name + " tamaño " + rock.GetComponent<Collider>().bounds.size);
            if (rock == null) { Debug.Log("AUTOTEST atasco: no hay rocas"); Application.Quit(); yield break; }
            var cc8 = player.GetComponent<CharacterController>();
            cc8.enabled = false;
            var rb = rock.GetComponent<Collider>().bounds;
            var inside = new Vector3(rb.center.x, 0f, rb.center.z);
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(inside, Quaternion.identity, Vector3.one);
            cc8.enabled = true;
            yield return new WaitForSeconds(0.3f);
            Shot("t1_dentro_de_roca");
            Debug.Log($"AUTOTEST atasco: metido en la roca en {inside}");
            yield return new WaitForSeconds(2.5f);
            Debug.Log($"AUTOTEST atasco: ahora en {player.transform.position}, distancia a la roca {Vector3.Distance(new Vector3(player.transform.position.x, 0, player.transform.position.z), inside):0.0} m");
            Shot("t2_liberado");
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-basicotest") >= 0)
        {
            // Ataque básico (clic) y a quién persigue el jefe durante 30 s (con los bots).
            yield return new WaitForSeconds(1f);
            var boss = BossAI.Active;
            var bh = boss.GetComponent<Health>();
            player.GetComponent<Health>().Invulnerable = true;
            var ab = player.GetComponent<PlayerAbilities>();
            var cc7 = player.GetComponent<CharacterController>();
            cc7.enabled = false;
            var near = boss.transform.position + boss.transform.forward * (boss.HitRadius + 2f);
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(near, Quaternion.LookRotation(boss.transform.position - near), Vector3.one);
            cc7.enabled = true;
            yield return new WaitForSeconds(0.5f);
            float hp = bh.Current.Value, ult = ab.UltCharge.Value;
            for (int i = 0; i < 3; i++) { ab.TestBasic(); yield return new WaitForSeconds(0.6f); if (i == 0) Shot("k1_basico"); }
            Debug.Log($"AUTOTEST básico: 3 golpes = {hp - bh.Current.Value:0} de daño (habilidad 1 hace {ab.Abilities[0].damage} por golpe), definitivo +{ab.UltCharge.Value - ult:0.0}");
            yield return new WaitForSeconds(30f);
            Shot("k2_jefe");
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-nuevotest") >= 0)
        {
            // 4.ª habilidad, punto débil, ataque conjunto y furia.
            yield return new WaitForSeconds(1f);
            var boss = BossAI.Active;
            boss.Paused = true;
            var bh = boss.GetComponent<Health>();
            player.GetComponent<Health>().Invulnerable = true;
            var ab = player.GetComponent<PlayerAbilities>();
            Debug.Log($"AUTOTEST habilidades: {ab.Abilities.Length}, la 4.ª desbloqueada={PlayerAbilities.IsUnlocked(ab.Abilities[3])}");
            yield return new WaitForSeconds(0.5f);
            Shot("n1_barra");
            float hp = bh.Current.Value;
            ab.TestUse(3);
            yield return new WaitForSeconds(0.3f);
            Shot("n2_habilidad4");
            yield return new WaitForSeconds(0.6f);
            Shot("n3_habilidad4_impacto");
            Debug.Log($"AUTOTEST daño habilidad 4: {hp - bh.Current.Value:0}");
            yield return new WaitForSeconds(1.2f);

            // Punto débil: el jugador se pone del lado correcto y golpea.
            boss.TestExpose();
            yield return new WaitForSeconds(0.5f);
            var side = boss.WeakSide.Value == 1 ? 1f : -1f;
            var cc6 = player.GetComponent<CharacterController>();
            cc6.enabled = false;
            var spot = boss.transform.position + boss.transform.forward * side * (boss.HitRadius + 2f);
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(spot, Quaternion.LookRotation(boss.transform.position - spot), Vector3.one);
            cc6.enabled = true;
            yield return new WaitForSeconds(0.4f);
            Shot("n4_punto_debil");
            hp = bh.Current.Value;
            ab.TestUse(0);
            yield return new WaitForSeconds(0.3f);
            Shot("n5_punto_debil_golpe");
            Debug.Log($"AUTOTEST punto débil: lado {boss.WeakSide.Value}, daño {hp - bh.Current.Value:0} (normal {ab.Abilities[0].damage})");
            yield return new WaitForSeconds(6f);

            // Ataque conjunto: jefe aturdido y otro golpe casi a la vez.
            boss.AddStun(bh.Max);
            yield return new WaitForSeconds(0.3f);
            boss.RegisterHit(424242UL);
            hp = bh.Current.Value;
            ab.TestUse(0);
            yield return new WaitForSeconds(0.3f);
            Shot("n6_conjunto");
            Debug.Log($"AUTOTEST conjunto: daño {hp - bh.Current.Value:0}");
            yield return new WaitForSeconds(2f);

            boss.Paused = false;
            boss.TestFury();
            yield return new WaitForSeconds(0.8f);
            Shot("n7_furia");
            Debug.Log($"AUTOTEST furioso={boss.Furious}");
            yield return new WaitForSeconds(1f);
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-clontest") >= 0)
        {
            // Réplicas del Chullachaqui que se mueven y atacan igual que el verdadero.
            yield return new WaitForSeconds(7f);
            player.GetComponent<Health>().Invulnerable = true;
            BossAI.Active.GetComponent<BossMechanics>().TestClones();
            for (int i = 1; i <= 5; i++)
            {
                yield return new WaitForSeconds(1.3f);
                Shot($"r{i}");
            }
            Debug.Log("AUTOTEST réplicas: " + GameObject.FindObjectsByType<CloneMirror>().Length);
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-muertetest") >= 0)
        {
            // Muerte épica del jefe: capturas a lo largo de la secuencia (tiempo real).
            yield return new WaitForSeconds(6f);
            var boss = BossAI.Active;
            boss.Paused = true;
            player.GetComponent<Health>().Invulnerable = true;
            yield return new WaitForSeconds(0.5f);
            boss.GetComponent<Health>().TakeDamage(999999f, 0);
            float[] waits = { 0.5f, 1f, 1f, 1f, 0.9f, 2.1f };
            for (int i = 0; i < waits.Length; i++)
            {
                yield return new WaitForSecondsRealtime(waits[i]);
                Shot($"m{i + 1}");
            }
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-contratest") >= 0)
        {
            // Esquiva perfecta → contraataque, y cadena de 3 golpes → remate.
            yield return new WaitForSeconds(6f);
            var boss = BossAI.Active;
            boss.Paused = true;
            var ph = player.GetComponent<Health>();
            var cc5 = player.GetComponent<CharacterController>();
            cc5.enabled = false;
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(
                boss.transform.position + boss.transform.forward * (boss.HitRadius + 2.5f), Quaternion.LookRotation(-boss.transform.forward), Vector3.one);
            cc5.enabled = true;
            yield return new WaitForSeconds(0.5f);
            var ab = player.GetComponent<PlayerAbilities>();
            float hp = ph.Current.Value;
            player.GetComponent<PlayerController>().TestDodge();
            yield return new WaitForSeconds(0.1f);
            ph.TakeDamage(30f, boss.NetworkObjectId);
            yield return new WaitForSecondsRealtime(0.15f);
            Shot("c1_esquiva_perfecta");
            Debug.Log($"AUTOTEST esquiva perfecta: vida {hp} → {ph.Current.Value}, contraataque listo={Time.time < ab.CounterReadyUntilLocal}");
            yield return new WaitForSecondsRealtime(1f);
            Shot("c2_contraataque_listo");
            yield return new WaitForSeconds(0.2f);
            ab.TestUse(0);
            yield return new WaitForSeconds(0.25f);
            Shot("c3_contraataque");
            yield return new WaitForSeconds(1f);
            ab.TestUse(0);
            yield return new WaitForSeconds(0.5f);
            Shot("c4_cadena2");
            Debug.Log($"AUTOTEST cadena: {ab.ComboCount.Value}");
            yield return new WaitForSeconds(0.2f);
            ab.TestUse(0);
            yield return new WaitForSeconds(0.25f);
            Shot("c5_remate");
            Debug.Log($"AUTOTEST tras remate cadena: {ab.ComboCount.Value}");
            yield return new WaitForSeconds(1.5f);
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-caidatest") >= 0)
        {
            // 1) Lo pone cerca del borde y lo hace "caminar" hacia afuera: el muro invisible debe frenarlo.
            yield return new WaitForSeconds(3f);
            var cc3 = player.GetComponent<CharacterController>();
            var nt = player.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            cc3.enabled = false; nt.Teleport(new Vector3(40f, 0.1f, 0f), Quaternion.identity, Vector3.one); cc3.enabled = true;
            yield return new WaitForSeconds(0.3f);
            for (float t = 0; t < 2.5f; t += Time.deltaTime) { cc3.Move(Vector3.right * 12f * Time.deltaTime + Vector3.down * 0.1f); yield return null; }
            Debug.Log($"AUTOTEST borde: x={player.transform.position.x:F1} y={player.transform.position.y:F1} (debe quedar < 46 y sobre el suelo)");
            Shot("c1_borde");
            // 2) Lo deja caer por debajo del suelo: la red de seguridad lo devuelve al inicio.
            cc3.enabled = false; nt.Teleport(new Vector3(0f, -8f, 0f), Quaternion.identity, Vector3.one); cc3.enabled = true;
            yield return new WaitForSeconds(1f);
            Debug.Log($"AUTOTEST caida: posición tras caer = {player.transform.position} (debe estar en el inicio, y > -1)");
            Shot("c2_tras_caida");
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-fasetest") >= 0)
        {
            // Prueba de lo nuevo: nombre, chat, aturdido, definitivo, segunda fase, ataques especiales y amuletos.
            yield return new WaitForSeconds(6f);
            var boss = BossAI.Active;
            var ph = player.GetComponent<Health>();
            ph.Invulnerable = true;
            var ab = player.GetComponent<PlayerAbilities>();
            ab.SetName("Maya");
            ab.TestChat(0);
            yield return new WaitForSeconds(0.5f);
            Shot("f1_nombre_chat");
            if (boss.Data.moveSpeed > 0f)
            {
                var cc2 = player.GetComponent<CharacterController>();
                cc2.enabled = false;
                var spot = boss.transform.position + boss.transform.forward * (boss.HitRadius + 2.5f);
                player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(spot, Quaternion.LookRotation(-boss.transform.forward), Vector3.one);
                cc2.enabled = true;
            }
            yield return new WaitForSeconds(1.5f);
            Shot("f2_ataque_jefe");
            boss.AddStun(99999f);
            yield return new WaitForSeconds(0.6f);
            Shot("f3_aturdido");
            ab.TestUlt();
            yield return new WaitForSeconds(0.45f);
            Shot("f4_definitivo");
            yield return new WaitForSeconds(3.5f);
            var bh = boss.GetComponent<Health>();
            bh.TakeDamage(bh.Current.Value - bh.Max * 0.45f, 0);
            yield return new WaitForSeconds(1.0f);
            Shot("f5_fase2");
            yield return new WaitForSeconds(3.2f);
            Shot("f6_especial1_aviso");
            yield return new WaitForSeconds(1.0f);
            Shot("f7_especial1");
            yield return new WaitForSeconds(8.2f);
            Shot("f8_especial2_aviso");
            yield return new WaitForSeconds(1.0f);
            Shot("f9_especial2");
            bh.TakeDamage(99999f, 0);
            yield return new WaitForSeconds(4.4f);
            Shot("f10_amuletos");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(args, "-fxtest") >= 0)
        {
            // Prueba de efectos: cada clase lanza sus 3 poderes junto al jefe (pausado) y se captura cada uno.
            yield return new WaitForSeconds(5f);
            var boss = BossAI.Active;
            boss.Paused = true;
            player.GetComponent<Health>().Invulnerable = true;
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            var spot = boss.transform.position + boss.transform.forward * (boss.HitRadius + 2f);
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(spot, Quaternion.LookRotation(-boss.transform.forward), Vector3.one);
            cc.enabled = true;
            var abilities = player.GetComponent<PlayerAbilities>();
            for (int r = 0; r < 5; r++)
            {
                abilities.ChooseRole(r);
                yield return new WaitForSeconds(0.6f);
                for (int i = 0; i < 3; i++)
                {
                    abilities.TestUse(i);
                    yield return new WaitForSeconds(r == 2 && i == 2 ? 0.45f : i == 2 ? 0.3f : 0.2f);
                    Shot($"fx_{r}_{i}");
                    yield return new WaitForSeconds(1.2f);
                }
            }
            Application.Quit();
            yield break;
        }
        if (BossAI.Active.Data.moveSpeed <= 0f)
        {
            // Jefe que no se mueve (Yacumama): el jugador se acerca a la orilla.
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(
                BossAI.Active.transform.position + BossAI.Active.transform.forward * 9f, Quaternion.LookRotation(-BossAI.Active.transform.forward), Vector3.one);
            cc.enabled = true;
        }

        // Lanza la mecánica del jefe apenas termina la presentación y captura el momento.
        yield return new WaitForSeconds(5f);
        BossAI.Active.GetComponent<BossMechanics>()?.TestTrigger();
        yield return new WaitForSeconds(2f);
        Shot("3b_mecanica");
        Debug.Log("AUTOTEST mecanica: " + BossMechanics.Announcement);
        yield return new WaitForSeconds(1f);
        Shot("3_combate");
        Debug.Log($"AUTOTEST jugador en {player.transform.position}, vida {player.GetComponent<Health>().Current.Value}");
        Debug.Log($"AUTOTEST jefe en {BossAI.Active.transform.position}, vida {BossAI.Active.GetComponent<Health>().Current.Value}");

        // Con dos jugadores: cae el aliado, el anfitrión va a su lado y lo revive.
        PlayerAbilities ally = null;
        foreach (var p in FindObjectsByType<PlayerAbilities>()) if (!p.IsOwner) ally = p;
        if (ally != null)
        {
            Debug.Log($"AUTOTEST jefe vida máxima con 2 jugadores: {BossAI.Active.GetComponent<Health>().Max}");
            ally.GetComponent<Health>().TakeDamage(99999f, 0);
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            var behind = ally.transform.position - (BossAI.Active.transform.position - ally.transform.position).normalized * 2f;
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(behind,
                Quaternion.LookRotation(Vector3.ProjectOnPlane(ally.transform.position - behind, Vector3.up)), Vector3.one);
            cc.enabled = true;
            yield return new WaitForSeconds(1f);
            Shot("9_revivir");
            yield return new WaitForSeconds(0.2f);   // la captura se toma al final del cuadro
            player.GetComponent<PlayerAbilities>().TestRevive(ally);
            yield return new WaitForSeconds(0.4f);
            Shot("9b_revivido");
            Debug.Log($"AUTOTEST aliado revivido con vida {ally.GetComponent<Health>().Current.Value}");
        }

        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-derrota") >= 0)
        {
            // Prueba de derrota: cae el único jugador.
            player.GetComponent<Health>().TakeDamage(99999f, 0);
            yield return new WaitForSeconds(1.2f);
            Shot("7_derrota");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
            yield break;
        }

        // Baja la vida del jefe al 25 % para que entre en furia.
        var bossHealth = BossAI.Active.GetComponent<Health>();
        bossHealth.TakeDamage(bossHealth.Max * 0.75f, 0);
        yield return new WaitForSeconds(2.7f);
        player.GetComponent<PlayerAbilities>().TestUse(1);   // el jugador ataca o cura justo antes de la captura
        yield return new WaitForSeconds(0.12f);
        Shot("4_ataque");
        yield return new WaitForSeconds(0.5f);

        BossAI.Active.GetComponent<Health>().TakeDamage(99999f, 0);
        yield return new WaitForSeconds(0.15f);
        Shot("5a_explosion");
        yield return new WaitForSeconds(2.5f);
        Shot("5_muerte");
        yield return new WaitForSeconds(2.2f);
        Shot("5b_nivel_superado");
        Debug.Log($"AUTOTEST progreso: {Progress.Unlocked} abiertos, {Progress.Beaten} vencidos");
        yield return new WaitForSeconds(0.2f);
        PauseMenu.SetOpen(true);
        yield return new WaitForSeconds(0.3f);
        Shot("6_pausa");
        PauseMenu.SetOpen(false);

        // En el último nivel: el anfitrión pasa a la victoria y luego vuelve al menú.
        if (level == LevelManager.Instance.levelScenes.Length - 1)
        {
            LevelManager.Instance.NextLevel();
            yield return new WaitForSeconds(3f);
            Shot("8_victoria");
            Debug.Log($"AUTOTEST créditos abiertos={Creditos.Open} canción={GameAudio.CreditsPlaying}");
            yield return new WaitForSecondsRealtime(1f);
            Creditos.Close();   // saltarlos
            yield return new WaitForSeconds(1.5f);
            Shot("8b_victoria_tras_creditos");
            LevelManager.Instance.ReturnToMenu();
            yield return new WaitForSeconds(2f);
            Shot("10_volver_al_menu");
        }

        yield return new WaitForSeconds(1f);
        Application.Quit();
    }

    static int WaitPlayers(string[] a) { int i = System.Array.IndexOf(a, "-esperar"); return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int n) ? n : 2; }

    void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
}

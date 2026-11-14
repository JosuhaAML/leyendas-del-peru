using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Configura los FBX de Mixamo de un jefe y crea su Animator Controller.
// Archivos esperados en Assets/Modelos/<nombre>/:
//   <nombre>.fbx (con piel, animación de quieto), <nombre>_Walk.fbx, _Attack.fbx, _Cast.fbx, _Die.fbx
public static class BossAnimSetup
{
    public static RuntimeAnimatorController Setup(string name)
    {
        string dir = $"Assets/Modelos/{name}";
        string skinPath = $"{dir}/{name}.fbx";
        if (!File.Exists(skinPath)) return null;

        // Cada FBX crea su propio avatar humanoide y Unity "retargetea" la animación entre esqueletos:
        // así sirven animaciones de Mixamo en modelos con esqueleto de AccuRIG (los héroes).
        ConfigureImporter(skinPath, null, loop: true);
        Avatar avatar = null;

        var controllerPath = $"{dir}/{name}.controller";
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        foreach (var trigger in new[] { "Attack", "Cast", "Enrage", "Die" })
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        // Quieto: <nombre>_Idle.fbx si existe (los modelos de AccuRIG vienen sin animación); si no, la del propio modelo.
        var idle = AddState(sm, "Idle", Clip(dir, name, "_Idle", avatar, true) ?? skinPath);
        sm.defaultState = idle;

        var walk = AddState(sm, "Walk", Clip(dir, name, "_Walk", avatar, true));
        if (walk != null)
        {
            var toWalk = idle.AddTransition(walk);
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            toWalk.duration = 0.15f;
            var toIdle = walk.AddTransition(idle);
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            toIdle.duration = 0.15f;
        }

        // Ataques: desde cualquier estado, y al terminar vuelven a quieto.
        AddAction(sm, idle, "Attack", Clip(dir, name, "_Attack", avatar, false), "Attack");
        var cast = AddAction(sm, idle, "Cast", Clip(dir, name, "_Cast", avatar, false), "Cast");
        if (cast != null)
        {
            var enrage = sm.AddAnyStateTransition(cast);
            enrage.AddCondition(AnimatorConditionMode.If, 0, "Enrage");
            enrage.duration = 0.1f;
        }

        var dieClip = Clip(dir, name, "_Die", avatar, false);
        if (dieClip != null)
        {
            var die = sm.AddState("Die");
            die.motion = AssetDatabase.LoadAllAssetsAtPath(dieClip).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var t = sm.AddAnyStateTransition(die);
            t.AddCondition(AnimatorConditionMode.If, 0, "Die");
            t.canTransitionToSelf = false;
            t.duration = 0.1f;
        }

        AssetDatabase.SaveAssets();
        return controller;
    }

    static string Clip(string dir, string name, string suffix, Avatar avatar, bool loop)
    {
        string path = $"{dir}/{name}{suffix}.fbx";
        if (!File.Exists(path)) return null;
        ConfigureImporter(path, avatar, loop);
        return path;
    }

    static void ConfigureImporter(string path, Avatar sourceAvatar, bool loop)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = sourceAvatar == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
        imp.sourceAvatar = sourceAvatar;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.loopTime = loop;
            c.lockRootPositionXZ = true;
            c.keepOriginalPositionXZ = true;
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    static AnimatorState AddState(AnimatorStateMachine sm, string stateName, string fbxPath)
    {
        if (fbxPath == null) return null;
        var state = sm.AddState(stateName);
        state.motion = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        return state;
    }

    static AnimatorState AddAction(AnimatorStateMachine sm, AnimatorState idle, string stateName, string fbxPath, string trigger)
    {
        var state = AddState(sm, stateName, fbxPath);
        if (state == null) return null;
        var enter = sm.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
        enter.canTransitionToSelf = false;
        enter.duration = 0.1f;
        var exit = state.AddTransition(idle);
        exit.hasExitTime = true;
        exit.exitTime = 0.9f;
        exit.duration = 0.15f;
        return state;
    }
}

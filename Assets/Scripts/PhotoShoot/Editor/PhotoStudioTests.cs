using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Nekolpos.PhotoShoot.Tests
{
    public sealed class PhotoStudioTests
    {
        [Test]
        public void PostSettingsApplyToRuntimeProfileAndPreventInvalidLensFocus()
        {
            var root=new GameObject("PhotoPostTest");root.SetActive(false);
            var source=ScriptableObject.CreateInstance<VolumeProfile>();
            var original=source.Add<DepthOfField>();
            source.Add<Bloom>();source.Add<ColorAdjustments>();
            var volume=root.AddComponent<Volume>();volume.sharedProfile=source;
            var controller=root.AddComponent<PhotoStudioController>();controller.postVolume=volume;
            controller.photoCamera=root.AddComponent<Camera>();
            var cameraData=root.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing=false;cameraData.requiresDepthOption=CameraOverrideOption.Off;cameraData.volumeLayerMask=0;
            try
            {
                controller.dofEnabled=true;controller.focusDistance=.1f;controller.focalLength=150;controller.aperture=2;
                controller.ApplyPostSettings();
                Assert.That(volume.profile.TryGet(out DepthOfField effective),Is.True);
                Assert.That(effective.mode.value,Is.EqualTo(DepthOfFieldMode.Bokeh));
                Assert.That(effective.focusDistance.value,Is.GreaterThan(.15f));
                Assert.That(effective.aperture.value,Is.EqualTo(2));
                Assert.That(original.mode.value,Is.EqualTo(DepthOfFieldMode.Off));
                Assert.That(cameraData.renderPostProcessing,Is.True);
                Assert.That(cameraData.requiresDepthOption,Is.EqualTo(CameraOverrideOption.On));
                Assert.That((cameraData.volumeLayerMask.value & (1 << volume.gameObject.layer)) != 0,Is.True);
                controller.dofEnabled=false;controller.ApplyPostSettings();
                Assert.That(effective.mode.value,Is.EqualTo(DepthOfFieldMode.Off));
                Assert.That(effective.mode.overrideState,Is.True);
            }
            finally
            {
                foreach(var component in volume.profile.components)UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(volume.profile);
                UnityEngine.Object.DestroyImmediate(root);
                foreach(var component in source.components)UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void BoneUndoAndRedoRemainAfterPoseReapplication()
        {
            var scene=EditorSceneManager.NewPreviewScene();scene.name="MainPhotoScene";
            var root=new GameObject("PhotoUndoTest");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
            var bone=new GameObject("head").transform;bone.SetParent(root.transform);
            var controller=root.AddComponent<PhotoStudioController>();controller.subject=root.transform;
            controller.animator=root.AddComponent<Animator>();
            controller.lookRig=root.AddComponent<Nekolpos.System.NekomataLookRigController>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(PhotoStudioController).GetField("ready",flags).SetValue(controller,true);
            ((global::System.Collections.Generic.List<Transform>)typeof(PhotoStudioController).GetField("editableBones",flags).GetValue(controller)).Add(bone);
            typeof(PhotoStudioController).GetMethod("AddBone",flags).Invoke(controller,new object[]{bone});
            typeof(PhotoStudioController).GetMethod("ObservePose",flags).Invoke(controller,null);
            var late=typeof(PhotoStudioController).GetMethod("LateUpdate",flags);
            var original=bone.localRotation;
            var edited=Quaternion.Euler(12,28,-8);
            try
            {
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(bone,"Photo bone regression");
                controller.SetBonePose(bone,bone.localPosition,edited);
                Undo.FlushUndoRecordObjects();
                late.Invoke(controller,null);
                Assert.That(Quaternion.Angle(bone.localRotation,edited),Is.LessThan(.01f));
                Undo.PerformUndo();controller.SynchronizeEditorBoneEdits();late.Invoke(controller,null);
                Assert.That(Quaternion.Angle(bone.localRotation,original),Is.LessThan(.01f),"Held pose must accept Undo.");
                Undo.PerformRedo();controller.SynchronizeEditorBoneEdits();late.Invoke(controller,null);
                Assert.That(Quaternion.Angle(bone.localRotation,edited),Is.LessThan(.01f),"Held pose must accept Redo.");
            }
            finally
            {
                Undo.ClearUndo(bone);UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void BoneEditingRejectsTransformsOutsideTheSubject()
        {
            var root=new GameObject("PhotoInvalidBoneTest");root.SetActive(false);
            var other=new GameObject("UnrelatedBone");
            var controller=root.AddComponent<PhotoStudioController>();controller.subject=root.transform;
            try
            {
                Assert.Throws<ArgumentException>(()=>controller.SetBonePose(other.transform,Vector3.one,Quaternion.Euler(1,2,3)));
                Assert.That(other.transform.localPosition,Is.EqualTo(Vector3.zero));
                Assert.That(controller.PosePaused,Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(other); }
        }

        [Test]
        public void ProfileEditsNeverChangeTheSource()
        {
            var source = ScriptableObject.CreateInstance<VolumeProfile>();
            var original = source.Add<Bloom>(); original.intensity.Override(.2f);
            var copy = PhotoStudioController.CloneProfile(source);
            try
            {
                Assert.That(copy.TryGet(out Bloom cloned), Is.True);
                cloned.intensity.Override(3f);
                Assert.That(original.intensity.value, Is.EqualTo(.2f));
                Assert.That(cloned, Is.Not.SameAs(original));
            }
            finally
            {
                foreach (var component in copy.components) UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(copy);
                foreach (var component in source.components) UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CameraPresetsPersistAndRejectInvalidValues()
        {
            var folder = Path.Combine(Path.GetTempPath(), "NekolposPhotoTest_" + Guid.NewGuid().ToString("N"));
            var root = new GameObject("PhotoPresetTest"); root.SetActive(false);
            var camera = root.AddComponent<Camera>();
            var first = root.AddComponent<PhotoStudioController>(); first.photoCamera = camera;
            var directory = typeof(PhotoStudioController).GetField("presetDirectory", BindingFlags.NonPublic | BindingFlags.Instance);
            directory.SetValue(first, folder);
            try
            {
                camera.transform.position = new Vector3(1, 2, 3); camera.fieldOfView = 47;
                first.SavePreset("A"); camera.fieldOfView = 32; first.SavePreset("B");
                var second = root.AddComponent<PhotoStudioController>(); second.photoCamera = camera;
                directory.SetValue(second, folder); second.LoadPresets();
                Assert.That(second.Presets.Count, Is.EqualTo(2));
                second.RestoreCamera(second.Presets[0]);
                Assert.That(camera.fieldOfView, Is.EqualTo(47));
                Assert.That(camera.transform.position, Is.EqualTo(new Vector3(1, 2, 3)));
                first.SavePreset("A"); second.LoadPresets();
                Assert.That(second.Presets.Count, Is.EqualTo(2), "Same name updates, rather than duplicating.");
                Assert.Throws<ArgumentException>(() => first.SavePreset("  "));
                File.WriteAllText(first.PresetFilePath, "{\"presets\":[{\"name\":\"bad\",\"fov\":0}]}");
                LogAssert.Expect(LogType.Warning, new Regex("Camera presets were not loaded"));
                second.LoadPresets();
                Assert.That(second.Presets.Count, Is.EqualTo(2), "Invalid data must retain the existing presets.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}

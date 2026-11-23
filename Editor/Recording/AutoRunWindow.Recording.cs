using System;
using System.IO;
using UnityAutorun.Recording;
using UnityEditor;
using UnityEngine;

public partial class AutoRunWindow
{
    [SerializeField] private bool _showRecording;
    private string _recordingError;

    private void RenderRecordingPanel()
    {
        _showRecording = EditorGUILayout.Foldout(_showRecording, "Game View Recording", true);
        if (!_showRecording) return;

        BeginPanel("Record Game View");
        AutoRunRecordingStatus active = AutoRunRecordingService.Active;
        if (active != null)
        {
            EditorGUILayout.LabelField(active.message, EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Stop & Save MP4"))
                AutoRunRecordingService.Stop(active.recordingId);
        }
        else
        {
            EditorGUILayout.LabelField("Keep the Game View visible in Play Mode.", EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling))
            {
                if (GUILayout.Button("Start Recording"))
                {
                    AutoRunRecordingStatus result = AutoRunRecordingService.Start(Guid.NewGuid().ToString("N"), null,
                        AutoRunRecordingService.DefaultFps, AutoRunRecordingService.DefaultMaxSeconds);
                    _recordingError = result.code == "ok" ? null : result.message;
                }
            }

            AutoRunRecordingStatus latest = AutoRunRecordingService.Latest;
            if (!string.IsNullOrEmpty(_recordingError))
                EditorGUILayout.HelpBox(_recordingError, MessageType.Error);
            else if (latest?.state == "failed")
                EditorGUILayout.HelpBox(latest.message, MessageType.Error);
            else if (latest?.state == "completed")
            {
                EditorGUILayout.LabelField(latest.outputPath, EditorStyles.wordWrappedLabel);
                using (new EditorGUI.DisabledScope(!File.Exists(latest.outputPath)))
                {
                    if (GUILayout.Button("Show Saved Video")) EditorUtility.RevealInFinder(latest.outputPath);
                }
            }
        }
        EndPanel();
    }
}

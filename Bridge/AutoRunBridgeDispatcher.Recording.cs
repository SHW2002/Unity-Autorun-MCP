using UnityAutorun.Recording;

public sealed partial class AutoRunBridgeDispatcher
{
    private static AutoRunBridgeResponse ExecuteRecording(AutoRunBridgeRequest request)
    {
        AutoRunBridgePayload payload = request.payload ?? new AutoRunBridgePayload();
        AutoRunRecordingStatus recording;
        switch (request.command)
        {
            case "start_recording":
                recording = AutoRunRecordingService.Start(payload.recordingId, payload.recordingPath,
                    payload.recordingFps, payload.recordingMaxSeconds);
                break;
            case "stop_recording":
                recording = AutoRunRecordingService.Stop(payload.recordingId);
                break;
            default:
                recording = AutoRunRecordingService.GetStatus(payload.recordingId);
                break;
        }

        bool ok = recording.code == "ok";
        var response = ok
            ? AutoRunBridgeResponses.Success(request.id, recording.message)
            : AutoRunBridgeResponses.Fail(request.id, recording.code, recording.message);
        response.data.recording = recording;
        return response;
    }
}

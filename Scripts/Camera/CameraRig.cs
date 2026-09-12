using Godot;

public partial class CameraRig : Node3D
{
    [Export] private Node3D _yawPivot;
    [Export] private Node3D _pitchPivot;

    private float _yaw = 0.0f;
    [Export(PropertyHint.Range, "-80,10,1")]
    private float _startingPitchDegrees = -15.0f;

    private float _pitch;

    public override void _Ready()
    {
        _pitch = Mathf.DegToRad(_startingPitchDegrees);
        ApplyRotation();
    }

    private void ApplyRotation()
    {
        _yawPivot.Rotation = new Vector3(0.0f, _yaw, 0.0f);
        _pitchPivot.Rotation = new Vector3(_pitch, 0.0f, 0.0f);
    }
}

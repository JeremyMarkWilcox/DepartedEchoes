using Godot;
using System;

public partial class CombatEncounter : Node3D
{
    [Export] private CameraRig _cameraRig = null!;
    [Export] private Player _player = null!;
    [Export] private Area3D _combatBoundary = null!;
    [Export] private Marker3D _combatCenter = null!;
}

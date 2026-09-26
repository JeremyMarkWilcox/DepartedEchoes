using Godot;

/// <summary>Optional authored participant slot. Game owns instantiation and placement.</summary>
public partial class EncounterSpawn : Marker3D
{
    [Export] public bool Enabled { get; set; }
    [Export] public PackedScene ParticipantScene { get; set; }
}

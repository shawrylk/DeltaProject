using DeltaProject.Infrastructure.Rendering;
using Godot;

[Tool]
public partial class GoldfishMesh : MeshInstance3D
{
    public override void _Ready() => Build();

    public ArrayMesh Build()
    {
        var mesh = GoldfishMeshBuilder.Build();
        Mesh = mesh;
        return mesh;
    }
}

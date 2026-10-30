#pragma warning disable CS0649

using System.Numerics;
using System.Runtime.InteropServices;

namespace IFSEngine.Rendering.GpuStructs;

[StructLayout(LayoutKind.Sequential)]
internal struct CameraStruct
{
    internal Matrix4x4 viewProjMatrix;
    internal Vector4 position;
    internal Vector4 forward;
    internal Vector4 focus_point;
    internal float aperture;
    internal float focus_distance;
    internal float depth_of_field;
    internal int projection_type;
}

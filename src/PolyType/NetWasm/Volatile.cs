#if NETWASM
namespace System.Threading;

// NetWasm (netwasm0.1) proof of concept: CoreLib's Volatile class is internal.
// NetWasm is single-threaded, so plain accesses suffice.
internal static class Volatile
{
    public static void Write(ref bool location, bool value) => location = value;

    public static bool Read(ref bool location) => location;
}
#endif

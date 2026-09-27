"""Turn on GPU rendering for Cycles in a background Blender run.

--factory-startup skips user preferences, which is where Blender keeps the GPU choice, so without this
Cycles renders on the CPU.
"""
import bpy


def enable_cycles_gpu(scene):
    """Use the first GPU backend with a device; leaves the CPU in place when none is found."""
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for backend in ("OPTIX", "CUDA", "HIP", "METAL", "ONEAPI"):
        try:
            prefs.compute_device_type = backend
        except TypeError:
            continue
        prefs.get_devices()
        gpus = [d for d in prefs.devices if d.type == backend]
        if gpus:
            for device in prefs.devices:
                device.use = device.type == backend
            scene.cycles.device = "GPU"
            print(f"rendering on {backend}: {', '.join(d.name for d in gpus)}")
            return
    print("no GPU found, rendering on the CPU")

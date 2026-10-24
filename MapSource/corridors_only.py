"""
Re-lays the corridor kit (notorious_corridors.py) on the faces the last full build saved, without rebuilding the map:

    blender -b --factory-startup -P MapSource/corridors_only.py

Writes the corridor chunk models and their list only. The map model itself still has the faces the last full build cut, so
run notorious_build.py when the cutting changes.
"""
import os
import sys
import pickle

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
src = open( os.path.join( HERE, "notorious_build.py" ), encoding="utf-8" ).read()
src = src[:src.rindex( "\nmain()" )]          # everything but the call that rebuilds the map
ns = { "__file__": os.path.join( HERE, "notorious_build.py" ), "__name__": "notorious_build_lib" }
exec( compile( src, "notorious_build.py", "exec" ), ns )
import bpy
import notorious_corridors as ncor
bpy.ops.wm.read_factory_settings( use_empty=True )
scratch = os.path.join( HERE, "__pycache__", "corridor_kit" )
render, phys, rects = pickle.load( open( os.path.join( scratch, "inputs.pkl" ), "rb" ) )
ncor.apply( render, phys, rects, ns["build_object"], ns["PROP_VMDL"], ns["PROP_PHYS"], ns["REV"], ns["MODEL_DIR"], ns["PROJECT"], scratch )

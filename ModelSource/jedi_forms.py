"""
Writes the six lightsaber form assets (Assets/data/jedi/forms/*.sform) from the built-in copies in Code/Jedi/SaberForms.cs,
so the forms can then be tuned in the editor's asset inspector (or by hand) without touching code:

    python ModelSource/jedi_forms.py
"""
import io
import json
import os
import re

ROOT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), ".." )
src = io.open( os.path.join( ROOT, "Code", "Jedi", "SaberForms.cs" ), encoding="utf-8" ).read()
block = src[src.index( "static List<SaberForm> BuiltIn()" ):]
out = os.path.join( ROOT, "Assets", "data", "jedi", "forms" )
os.makedirs( out, exist_ok=True )


def num( text ):
	return float( text.strip().rstrip( "f" ) )


for body in re.findall( r"new SaberForm\s*\{(.*?)\n\t\t\},", block, re.S ):
	d = {}
	for key, value in re.findall( r"(\w+) = (\"(?:[^\"\\]|\\.)*\"|true|false|-?[\d.]+f?)", body ):
		if value.startswith( '"' ): d[key] = json.loads( value )
		elif value in ( "true", "false" ): d[key] = value == "true"
		elif value.endswith( "f" ) or "." in value: d[key] = num( value )
		else: d[key] = int( value )
	hand = re.search( r"GuardHand = new\( ([^)]*) \)", body )
	if hand: d["GuardHand"] = ",".join( f"{num( x ):g}" for x in hand.group( 1 ).split( "," ) )
	combo = re.search( r"Combo = \{(.*?)\},", body, re.S ).group( 1 )
	d["Combo"] = [{ "Direction": m[0], "Speed": num( m[1] ), "Damage": num( m[2] ), "Arc": num( m[3] ) }
		for m in re.findall( r"new\( SwingDir\.(\w+), ([\d.]+f?), ([\d.]+f?), ([\d.]+f?) \)", combo )]
	d["Version"] = int( re.search( r"DataVersion = (\d+)", src ).group( 1 ) )
	d.setdefault( "Requires", "" )
	d.setdefault( "OneHanded", False )
	path = os.path.join( out, d["Id"] + ".sform" )
	io.open( path, "w", encoding="utf-8", newline="\n" ).write( json.dumps( d, indent=2 ) )
	print( os.path.relpath( path, ROOT ), len( d["Combo"] ), "steps" )

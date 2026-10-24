#pragma warning disable CS0618 // ScenePanel.World/Camera are marked obsolete but are still the supported way to show a standalone model
using System;

namespace Swrp;

/// <summary>
/// A small turntable that shows a class's playermodel. Owns its own SceneWorld, so it needs nothing from the game scene.
/// </summary>
public sealed class ClassPreview : Sandbox.UI.Panel
{
	public string ClassId { get; set; }

	/// <summary> A Jedi's Appearance text: picks the head and robe bodygroups (SwrpOutfit). </summary>
	public string Look { get; set; }

	/// <summary> The battalion: its markings are the model's material group of that name (SwrpOutfit.GroupFor). </summary>
	public string Faction { get; set; }
	string _shownFaction;
	RealTimeSince _sinceModelCheck;

	readonly SceneWorld _world;
	readonly Sandbox.UI.ScenePanel _view;
	SceneModel _model;
	string _shown;
	string _shownLook;
	float _yaw = 200f;

	public ClassPreview()
	{
		_world = new SceneWorld();

		_view = AddChild<Sandbox.UI.ScenePanel>();
		_view.World = _world;
		_view.Style.Width = Sandbox.UI.Length.Percent( 100 );
		_view.Style.Height = Sandbox.UI.Length.Percent( 100 );
		// Framed so the feet land on the hologram base at the bottom of the tall stage.
		var eye = new Vector3( 92f, 0f, 44f );
		_view.Camera.Position = eye;
		_view.Camera.Rotation = Rotation.LookAt( new Vector3( 0f, 0f, 40f ) - eye );
		_view.Camera.FieldOfView = 34f;
		_view.Camera.ZNear = 2f;
		_view.Camera.BackgroundColor = Color.Transparent;
		_view.Camera.AmbientLightColor = new Color( 0.25f, 0.28f, 0.34f );

		new SceneDirectionalLight( _world, Rotation.From( 40f, 160f, 0f ), new Color( 1.0f, 0.97f, 0.92f ) * 2.2f );
		new SceneDirectionalLight( _world, Rotation.From( 25f, -40f, 0f ), new Color( 0.45f, 0.6f, 1.0f ) * 1.1f );
		// Cyan rim from behind, for the hologram look.
		new SceneDirectionalLight( _world, Rotation.From( 15f, 0f, 0f ), new Color( 0.35f, 0.8f, 1.0f ) * 1.6f );
	}

	public override void Tick()
	{
		base.Tick();

		if ( _shown != ClassId )
			Rebuild();

		// a rebuilt model (or one that failed to load before) is picked up without reopening the menu
		if ( _sinceModelCheck > 1f )
		{
			_sinceModelCheck = 0;
			if ( ClassRegistry.Get( ClassId )?.LoadModel() is { } current && current != _model?.Model ) Rebuild();
		}

		if ( _model is null ) return;

		if ( _shownFaction != Faction )
		{
			_shownFaction = Faction;
			_model.SetMaterialGroup( SwrpOutfit.GroupFor( _model.Model, Faction ) );
		}

		if ( _shownLook != Look )
		{
			_shownLook = Look;
			if ( Look is not null && _model.Model.Parts.All.Any( p => p.Name == "head" ) )
			{
				var (head, robe) = SwrpOutfit.JediParts( Look );
				_model.SetBodyGroup( "head", head );
				_model.SetBodyGroup( "robe", robe );
			}
		}

		_yaw += Time.Delta * 18f;
		_model.Transform = new Transform( Vector3.Zero, Rotation.FromYaw( _yaw ) );
		_model.Update( Time.Delta );
	}

	void Rebuild()
	{
		_shown = ClassId;
		_model?.Delete();
		_model = null;

		var model = ClassRegistry.Get( ClassId )?.LoadModel();
		if ( model is null ) return;

		_model = new SceneModel( _world, model, Transform.Zero );
		_shownLook = null;
		_shownFaction = null;
	}

	public override void OnDeleted()
	{
		_model?.Delete();
		_world?.Delete();
		base.OnDeleted();
	}
}

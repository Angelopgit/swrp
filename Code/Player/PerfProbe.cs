using System;

namespace Swrp;

/// <summary>
/// Logs the average frame rate every few seconds, so performance changes can be measured instead of guessed.
/// Enable with the console variable <c>swrp_perflog 1</c> (or leave the component on a scene object).
/// </summary>
[Title( "Perf Probe" ), Category( "SWRP" ), Icon( "speed" )]
public sealed class PerfProbe : Component
{
	[Property] public float Interval { get; set; } = 3f;

	float _time;
	int _frames;
	float _worst;

	protected override void OnUpdate()
	{
		_time += Time.Delta;
		_frames++;
		_worst = MathF.Max( _worst, Time.Delta );

		if ( _time < Interval ) return;

		Log.Info( $"[PERF] {_frames / _time:0.0} fps avg, worst frame {_worst * 1000f:0} ms" );
		_time = 0;
		_frames = 0;
		_worst = 0;
	}
}

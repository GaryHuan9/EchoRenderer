﻿using Echo.Core.Common.Compute;
using Echo.Core.Evaluation.Operation;
using Echo.Core.InOut;
using Echo.Core.Scenic.Hierarchies;
using Echo.Core.Scenic.Preparation;
using Echo.Terminal.Application;
using Echo.Terminal.Application.Report;
using Echo.Terminal.Core.Interface;

namespace Echo.Terminal;

public class EchoTI : RootTI
{
	public EchoTI()
	{
		Child0 = new CommandTI();
		Child1 = new BisectionTI
		{
			Horizontal = true,
			Balance = 0.48f,

			Child0 = new TileReportTI(),
			Child1 = new BisectionTI
			{
				Horizontal = true,
				Balance = 0.55f,

				Child0 = new OperationReportTI(),
				Child1 = new DeviceReportTI()
			}
		};

		Balance = 0.16f;

		device = Device.Create();
	}

	readonly Device device;

	public override void ProcessArguments(string[] arguments)
	{
		base.ProcessArguments(arguments);

		var objects = new EchoChronicleHierarchyObjects("ext/Scenes/SingleBunny/bunny.echo");

		var scene = objects.ConstructFirst<Scene>();
		var preparer = new ScenePreparer(scene);
		var profile = objects.ConstructFirst<EvaluationProfile>();

		var operation = new EvaluationOperation.Factory
		{
			NextScene = preparer.Prepare(),
			NextProfile = profile
		};

		device.Dispatch(operation);
	}

	// public override void Update(in Moment moment)
	// {
	// 	base.Update(in moment);
	//
	// 	while (Console.KeyAvailable)
	// 	{
	// 		ConsoleKeyInfo key = Console.ReadKey(true);
	// 		if (key.KeyChar == 'p') device.Pause();
	// 		if (key.KeyChar == 'r') device.Resume();
	// 	}
	// }

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing) device?.Dispose();
	}
}
﻿using System;
using System.Diagnostics;
using CodeHelpers.Packed;
using Echo.Common.Compute;
using Echo.Core.Evaluation.Distributions.Continuous;
using Echo.Core.Evaluation.Evaluators;
using Echo.Core.Evaluation.Operations;
using Echo.Core.Scenic.Examples;
using Echo.Core.Scenic.Preparation;
using Echo.Core.Textures.Evaluation;
using Echo.Core.Textures.Grid;
using Echo.UserInterface.Backend;
using ImGuiNET;

namespace Echo.UserInterface.Core.Areas;

public class SystemUI : AreaUI
{
	public SystemUI() : base("System") { }

	Device device;

	bool HasDevice => device is { Disposed: false };

	protected override void Draw(in Moment moment)
	{
		ImGui.Text(Environment.OSVersion.VersionString);
		ImGui.Text(Debugger.IsAttached ? "Debugger Attached" : "Debugger Not Attached");

#if DEBUG
		ImGui.Text("DEBUG Mode");
#elif RELEASE
		ImGui.Text("RELEASE Mode");
#else
		ImGui.Text("Unidentified Mode");
#endif

		ImGui.NewLine();

		ImGui.SetNextItemOpen(true, ImGuiCond.Once);
		if (ImGui.CollapsingHeader("Garbage Collector")) DrawGarbageCollector();

		ImGui.SetNextItemOpen(true, ImGuiCond.Once);
		if (ImGui.CollapsingHeader("Device and Workers"))
		{
			if (!HasDevice)
			{
				if (ImGui.Button("Create and Dispatch"))
				{
					device = Device.Create();
					DispatchDevice(device);
				}

				ImGui.SameLine();
				if (ImGui.Button("Create")) device = Device.Create();
				ImGui.TextWrapped("Create a compute device to begin!");
			}
			else DrawDevice();

			if (HasDevice) DrawWorkers();
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing) device?.Dispose();
	}

	void DrawGarbageCollector()
	{
		var info = GC.GetGCMemoryInfo();

		if (ImGui.Button("Collect All Generations")) GC.Collect();

		//Main table
		if (ImGuiCustom.BeginProperties("Main"))
		{
			ImGuiCustom.Property("GC Compacted", info.Compacted.ToString());
			ImGuiCustom.Property("GC Concurrent", info.Concurrent.ToString());
			ImGuiCustom.Property("GC Generation", info.Generation.ToString());

			ImGuiCustom.Property("Mapped Memory", ((ulong)Environment.WorkingSet).ToStringData());
			ImGuiCustom.Property("Heap Size", ((ulong)info.HeapSizeBytes).ToStringData());
			ImGuiCustom.Property("Available Memory", ((ulong)info.TotalAvailableMemoryBytes).ToStringData());
			ImGuiCustom.Property("Pinned Object Count", info.PinnedObjectsCount.ToString());
			ImGuiCustom.Property("Promoted Memory", ((ulong)info.PromotedBytes).ToStringData());
			ImGuiCustom.Property("GC Block Percentage", info.PauseTimePercentage.ToStringPercentage());
			ImGuiCustom.Property("GC Fragmentation", ((ulong)info.FragmentedBytes).ToStringData());

			ImGuiCustom.EndProperties();
		}

		//Generations table
		if (ImGui.BeginTable("Generations", 5, ImGuiCustom.DefaultTableFlags))
		{
			var generations = info.GenerationInfo;

			ImGui.TableSetupColumn("Generation");
			ImGui.TableSetupColumn("Size Before");
			ImGui.TableSetupColumn("Size After");
			ImGui.TableSetupColumn("Frag. Before");
			ImGui.TableSetupColumn("Frag. After");
			ImGui.TableHeadersRow();

			for (int i = 0; i < generations.Length; i++)
			{
				ref readonly GCGenerationInfo generation = ref generations[i];

				ImGuiCustom.TableItem((i + 1).ToString());
				ImGuiCustom.TableItem(((ulong)generation.SizeBeforeBytes).ToStringData());
				ImGuiCustom.TableItem(((ulong)generation.SizeAfterBytes).ToStringData());
				ImGuiCustom.TableItem(((ulong)generation.FragmentationBeforeBytes).ToStringData());
				ImGuiCustom.TableItem(((ulong)generation.FragmentationAfterBytes).ToStringData());
			}

			ImGui.EndTable();
		}
	}

	void DrawDevice()
	{
		//Buttons
		bool idle = device.IsIdle;
		ImGui.BeginDisabled(!idle);

		if (ImGui.Button("Dispatch")) DispatchDevice(device);

		ImGui.EndDisabled();
		ImGui.BeginDisabled(idle);

		ImGui.SameLine();
		if (ImGui.Button("Pause")) device.Pause();

		ImGui.SameLine();
		if (ImGui.Button("Resume")) device.Resume();

		ImGui.EndDisabled();
		ImGui.SameLine();

		if (ImGui.Button("Dispose"))
		{
			DisposeDevice(device);
			device = null;
			return;
		}

		//Status
		if (ImGuiCustom.BeginProperties("Main"))
		{
			ImGuiCustom.Property("State", device.IsIdle ? "Idle" : "Running");
			ImGuiCustom.Property("Population", device.Population.ToStringDefault());

			var operations = device.PastOperations;

			if (operations.Length > 0)
			{
				ImGuiCustom.Property("Latest Dispatch", operations[^1].creationTime.ToStringDefault());
				ImGuiCustom.Property("Past Operation Count", operations.Length.ToStringDefault());
			}
			else
			{
				ImGuiCustom.Property("Latest Dispatch", "None");
				ImGuiCustom.Property("Past Operation Count", "0");
			}

			ImGuiCustom.EndProperties();
		}
	}

	void DrawWorkers()
	{
		//Worker table
		if (ImGui.BeginTable("State", 3, ImGuiCustom.DefaultTableFlags))
		{
			ImGui.TableSetupColumn("Index");
			ImGui.TableSetupColumn("State");
			ImGui.TableSetupColumn("Guid");
			ImGui.TableHeadersRow();

			foreach (IWorker worker in device.Workers)
			{
				ImGuiCustom.TableItem($"0x{worker.Index:X4}");
				ImGuiCustom.TableItem(worker.State.ToDisplayString());
				ImGuiCustom.TableItem(worker.Guid.ToStringShort());
			}

			ImGui.EndTable();
		}
	}

	static void DispatchDevice(Device device)
	{
		ActionQueue.Enqueue(Dispatch, "Device Dispatch");

		void Dispatch()
		{
			var scene = new SingleBunny();

			var prepareProfile = new ScenePrepareProfile();

			var evaluationProfile = new TiledEvaluationProfile
			{
				Scene = new PreparedScene(scene, prepareProfile),
				Evaluator = new PathTracedEvaluator(),
				Distribution = new StratifiedDistribution { Extend = 64 },
				Buffer = new RenderBuffer(new Int2(960, 540)),
				Pattern = new OrderedPattern(),
				MinEpoch = 1,
				MaxEpoch = 1
			};

			var operation = new TiledEvaluationFactory
			{
				NextProfile = evaluationProfile
			};

			device.Dispatch(operation);
		}
	}

	static void DisposeDevice(Device device) => ActionQueue.Enqueue(device.Dispose, "Device Dispose");
}
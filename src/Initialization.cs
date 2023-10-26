using System.Collections.Immutable;
using System.IO;
using System.Reactive.Linq;
using VL.Core;
using VL.Core.CompilerServices;


// Tell VL where to find our initializer
[assembly: AssemblyInitializer(typeof(VL.RunwayML.Initialization))]

namespace VL.RunwayML
{
    public class Initialization : AssemblyInitializer<Initialization>
    {
        const string runwaySubDir = "runway";
        const string runwayHosted = "hosted-models.txt";
        const string runwayLocal = "local-models.txt";

        protected override void RegisterServices(IVLFactory factory)
        {
            factory.RegisterNodeFactory("VL.RunwayML-Factory", (directory, nodeFactory) =>
            {
                // In case "runway" directory gets added or deleted invalidate the whole factory
                var invalidated = NodeBuilding.WatchDir(directory)
                    .Where(e => (e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Deleted || e.ChangeType == WatcherChangeTypes.Renamed) && e.Name == runwaySubDir);

                var builder = ImmutableArray.CreateBuilder<IVLNodeDescription>();
                var runwayDirectory = Path.Combine(directory, runwaySubDir);
                if (Directory.Exists(runwayDirectory))
                {
                    // Additionaly watch out for new/deleted/renamed files
                    invalidated = invalidated.Merge(
                        NodeBuilding.WatchDir(runwayDirectory)
                        .Where(e => string.Equals(e.Name, runwayHosted, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Name, runwayLocal, StringComparison.OrdinalIgnoreCase)));

                    if (File.Exists(Path.Combine(runwayDirectory, runwayHosted)))
                    {
                        var runway = File.ReadAllText(Path.Combine(runwayDirectory, runwayHosted));

                        var hostedModels = runway.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var model in hostedModels)
                        {
                            var infos = model.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).ToList();
                            builder.Add(new ModelDescription(nodeFactory, infos[0], infos[1], infos[2]));
                        }
                    }

                    if (File.Exists(Path.Combine(runwayDirectory, runwayLocal)))
                    {
                        var runway = File.ReadAllText(Path.Combine(runwayDirectory, runwayLocal));

                        var localModels = runway.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var model in localModels)
                        {
                            var infos = model.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).ToList();
                            builder.Add(new ModelDescription(nodeFactory, infos[0], infos[1]));
                        }
                    }
                }

                return new(builder.ToImmutable(), invalidated);
            });
        }
    }
}

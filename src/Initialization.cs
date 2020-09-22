using VL.Core;
using VL.Core.CompilerServices;


// Tell VL where to find our initializer
[assembly: AssemblyInitializer(typeof(VL.RunwayML.Initialization))]

namespace VL.RunwayML
{
    public class Initialization : AssemblyInitializer<Initialization>
    {
        protected override void RegisterServices(IVLFactory factory)
        {
            factory.RegisterNodeFactory(runwayFactory);
        }

        static IVLNodeDescriptionFactory runwayFactory = new RunwayMLFactory();
    }
}

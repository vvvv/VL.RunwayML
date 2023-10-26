using System.Diagnostics;
using System.Net;
using System.Text;
using VL.Core;
using VL.Core.Diagnostics;
using Newtonsoft.Json;
using VL.Lib.Basics.Imaging;
using VL.Lib.Collections;
using Stride.Core.Mathematics;
using System.Reactive.Linq;

namespace VL.RunwayML
{
    sealed class ModelDescription : IVLNodeDescription, IInfo
    {
        bool FInitialized;
        bool FNotFound;
        bool FIsLocal;

        string FUrl;
        string FFullName;
        string? FSummary;
        public string FToken;
        string InfoUrl => FUrl + "info";
        public string QueryUrl => FUrl + "query";
        List<ModelPinDescription> inputs = new List<ModelPinDescription>();
        List<ModelPinDescription> outputs = new List<ModelPinDescription>();

        public ModelDescription(IVLNodeDescriptionFactory factory, string name, string url, string token = "")
        {
            Factory = factory;
            FFullName = name;
            Name = name.Split('/').Last();
            FUrl = url.Trim('/') + '/';
            FToken = token;
            FIsLocal = string.IsNullOrWhiteSpace(token) ? true : false;
        }

        void Init()
        {
            if (FInitialized)
                return;

            try
            {
                var httpRequest = (HttpWebRequest)WebRequest.CreateHttp(InfoUrl);
                httpRequest.Accept = "application/json";
                httpRequest.ContentType = "application/json";
                if (!string.IsNullOrWhiteSpace(FToken))
                    httpRequest.Headers.Add("Authorization", "Bearer " + FToken);
                var rstream = httpRequest.GetResponse().GetResponseStream();
                string modelInfo = "";
                using (var reader = new StreamReader(rstream, Encoding.UTF8, true, 0x1000, leaveOpen: true))
                {
                    modelInfo = reader.ReadToEnd();
                }
                dynamic? model = JsonConvert.DeserializeObject(modelInfo);

                if (model is null)
                    return;

                FSummary = model.description;

                Type type = typeof(object);
                object dflt = "";
                string desc = "";
                foreach (var input in model.inputs)
                {
                    GetTypeDefaultAndDescription(input, ref type, ref dflt, ref desc);
                    inputs.Add(new ModelPinDescription(input.name.ToString(), type, dflt, desc));
                }

                inputs.Add(new ModelPinDescription("Query", typeof(bool), false, "Sends a query every frame as long as enabled"));

                foreach (var output in model.outputs)
                {
                    GetTypeDefaultAndDescription(output, ref type, ref dflt, ref desc);
                    outputs.Add(new ModelPinDescription(output.name.ToString(), type, dflt, desc));
                }

                FInitialized = true;
                FNotFound = false;
            }
            catch (Exception e)
            {
                if (e.Message.Contains("(404)"))
                    FNotFound = true;
            }
        }

        void GetTypeDefaultAndDescription(dynamic pin, ref Type type, ref object? dflt, ref string desc)
        {
            desc = pin.description;

            if (pin.type == "text" || pin.type == "dropdown" || pin.type == "category")
            {
                type = typeof(string);
                dflt = pin.@default.ToString();
            }
            else if (pin.type == "number" || pin.type == "slider")
            {
                int step;
                if (int.TryParse(pin.step?.ToString() ?? pin.@default.ToString(), out step))
                {
                    type = typeof(int);
                    int d;
                    if (int.TryParse(pin.@default.ToString(), out d))
                        dflt = d;
                }
                else
                {
                    type = typeof(float);
                    float d;
                    if (float.TryParse(pin.@default.ToString(), out d))
                        dflt = d;
                }

                desc += "\r\nMin: " + pin.min + " Max: " + pin.max;
            }
            else if (pin.type == "boolean")
            {
                type = typeof(bool);
                bool d;
                if (bool.TryParse(pin.@default.ToString(), out d))
                    dflt = d;
            }
            else if (pin.type == "vector")
            {
                type = typeof(Spread<float>);
                dflt = Enumerable.Repeat<float>(0, (int)pin.length).ToArray();
            }
            else if (pin.type == "array") //assumes outputs for now
            {
                if (pin.itemType.type == "text")
                {
                    dflt = Enumerable.Repeat<string>("", 0).ToArray();
                    type = typeof(IEnumerable<string>);
                }
                else if (pin.itemType.type == "number")
                {
                    dflt = Enumerable.Repeat<float>(0, 0).ToArray();
                    type = typeof(IEnumerable<float>);
                }
                else if (pin.itemType.type == "image_bounding_box")
                {
                    dflt = Enumerable.Repeat<RectangleF>(RectangleF.Empty, 0).ToArray();
                    type = typeof(IEnumerable<RectangleF>);
                }
                else if (pin.itemType.type == "image_landmarks")
                {
                    dflt = Enumerable.Repeat<IEnumerable<Vector2>>(Enumerable.Repeat<Vector2>(new Vector2(), 0), 0);
                    type = typeof(IEnumerable<IEnumerable<Vector2>>);
                }
            }
            else if (pin.type == "image" || pin.type == "segmentation")
            {
                type = typeof(IImage);
                dflt = null;
            }
        }

        public IVLNodeDescriptionFactory Factory { get; }

        public string Name { get; }

        public string Category => "ML.RunwayML";

        public bool Fragmented => false;

        public IReadOnlyList<ModelPinDescription> Inputs
        {
            get
            {
                Init();
                return inputs;
            }
        }

        public IReadOnlyList<ModelPinDescription> Outputs
        {
            get
            {
                Init();
                return outputs;
            }
        }

        public IEnumerable<Core.Diagnostics.Message> Messages
        {
            get
            {
                if (FNotFound)
                    yield return new Message(MessageType.Warning, "Model inactive: " + FUrl + "\r\nActivate it in your RunwayML dashboard and then restart vvvv.");
                else
                    yield break;
            }
        }

        public string? Summary => FSummary;

        public string Remarks => "";

        public IObservable<object> Invalidated => Observable.Empty<object>();

        public IVLNode CreateInstance(NodeContext context)
        {
            return new ModelNode(this, context);
        }

        public bool OpenEditor()
        {
            if (!FIsLocal)
                Process.Start("https://app.runwayml.com/models/" + FFullName);
            return true;
        }

        IReadOnlyList<IVLPinDescription> IVLNodeDescription.Inputs => Inputs;
        IReadOnlyList<IVLPinDescription> IVLNodeDescription.Outputs => Outputs;
    }
}

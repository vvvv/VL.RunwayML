using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using VL.Core;
using VL.Core.Diagnostics;
using VL.RunwayML;
using Newtonsoft.Json;
using VL.Lib.Basics.Imaging;
using VL.Lang.PublicAPI;
using VL.Skia;
using SkiaSharp;
using VL.Lib.Collections;
using Stride.Core.Mathematics;
using System.Reactive.Linq;

namespace VL.RunwayML
{
    public class RunwayMLFactory : IVLNodeDescriptionFactory
    {
        const string runwayHosted = "hosted-models.txt";
        const string runwayLocal = "local-models.txt";
        const string runwaySubDir = "runway";
        const string identifier = "VL.RunwayML-Factory";

        public readonly string Directory;
        public readonly string DirectoryToWatch;

        // The node factory cache will invalidate in case a cached factory or one of its nodes invalidate
        private readonly NodeFactoryCache factoryCache = new NodeFactoryCache();

        public RunwayMLFactory(string directory = default, string directoryToWatch = default)
        {
            Directory = directory;
            DirectoryToWatch = directoryToWatch;

            var builder = ImmutableArray.CreateBuilder<IVLNodeDescription>();
            if (directory != null)
            {
                if (File.Exists(Path.Combine(directory, runwayHosted)))
                {
                    var runway = File.ReadAllText(Path.Combine(directory, runwayHosted));

                    var hostedModels = runway.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var model in hostedModels)
                    {
                        var infos = model.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).ToList();
                        builder.Add(new ModelDescription(this, infos[0], infos[1], infos[2]));
                    }
                }

                if (File.Exists(Path.Combine(directory, runwayLocal)))
                {
                    var runway = File.ReadAllText(Path.Combine(directory, runwayLocal));

                    var localModels = runway.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var model in localModels)
                    {
                        var infos = model.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).ToList();
                        builder.Add(new ModelDescription(this, infos[0], infos[1]));
                    }
                }
            }
            NodeDescriptions = builder.ToImmutable();
        }

        public ImmutableArray<IVLNodeDescription> NodeDescriptions { get; }

        public string Identifier
        {
            get
            {
                if (Directory != null)
                    return GetIdentifierForPath(Directory);
                else
                    return identifier;
            }
        }

        public IObservable<object> Invalidated
        {
            get
            {
                if (Directory != null)
                {
                    return NodeBuilding.WatchDir(Directory)
                        .Where(e => string.Equals(e.Name, runwayHosted, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Name, runwayLocal, StringComparison.OrdinalIgnoreCase));
                }
                else if (DirectoryToWatch != null)
                {
                    return NodeBuilding.WatchDir(DirectoryToWatch)
                        .Where(e => e.Name == runwaySubDir);
                }
                else
                {
                    return Observable.Empty<object>();
                }
            }
        }

        public void Export(ExportContext exportContext)
        {

        }

        string GetIdentifierForPath(string path) => $"{identifier} ({path})";

        public IVLNodeDescriptionFactory ForPath(string path)
        {
            // VL caches node factories per compilation only, not across compilations -> use our own cache
            var identifier = GetIdentifierForPath(path);
            return factoryCache.GetOrAdd(identifier, () =>
            {
                var runwayDir = Path.Combine(path, runwaySubDir);
                if (System.IO.Directory.Exists(runwayDir))
                    return new RunwayMLFactory(runwayDir);
                return new RunwayMLFactory(directoryToWatch: path);
            });
        }

        class ModelPinDescription : IVLPinDescription, IInfo
        {
            static string BeautifyPin(string s)
            {
                if (string.IsNullOrEmpty(s))
                {
                    return string.Empty;
                }

                var parts = s.Split('_');
                var result = "";
                foreach (var part in parts)
                {
                    char[] a = part.ToCharArray();
                    a[0] = char.ToUpper(a[0]);
                    result += new string(a) + " ";
                }
                return result.Trim();
            }

            public ModelPinDescription(string name, Type type, object defaultValue, string description)
            {
                Name = BeautifyPin(name);
                OriginalName = name;
                Type = type;
                DefaultValue = defaultValue;
                Summary = description;
            }

            public string Name { get; }
            public string OriginalName { get; }
            public Type Type { get; }
            public object DefaultValue { get; }

            public string Summary { get; }

            public string Remarks => "";
        }

        class ModelDescription : IVLNodeDescription, IInfo
        {
            bool FInitialized;
            bool FNotFound;
            bool FIsLocal;

            string FUrl;
            string FFullName;
            string FSummary;
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
                    dynamic model = JsonConvert.DeserializeObject(modelInfo);

                    FSummary = model.description;

                    Type type = typeof(object);
                    object dflt = "";
                    string name = "";
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

            void GetTypeDefaultAndDescription(dynamic pin, ref Type type, ref object dflt, ref string desc)
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

            public IReadOnlyList<IVLPinDescription> Inputs
            {
                get
                {
                    Init();
                    return inputs;
                }
            }

            public IReadOnlyList<IVLPinDescription> Outputs
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

            public string Summary => FSummary;

            public string Remarks => "";

            public IObservable<object> Invalidated => Observable.Empty<object>();

            public IVLNode CreateInstance(NodeContext context)
            {
                return new MyNode(this, context);
            }

            public bool OpenEditor()
            {
                if (!FIsLocal)
                    Process.Start("https://app.runwayml.com/models/" + FFullName);
                return true;
            }
        }

        class MyNode : VLObject, IVLNode
        {
            class MyPin : IVLPin
            {
                public object Value { get; set; }
                public Type Type { get; set; }
                public string Name { get; set; }
                public string OriginalName { get; set; }
            }

            readonly ModelDescription description;

            public MyNode(ModelDescription description, NodeContext nodeContext) : base(nodeContext)
            {
                this.description = description;
                Inputs = description.Inputs.Select(p => new MyPin() { Name = p.Name, OriginalName = ((ModelPinDescription)p).OriginalName, Type = p.Type, Value = p.DefaultValue }).ToArray();
                Outputs = description.Outputs.Select(p => new MyPin() { Name = p.Name, OriginalName = ((ModelPinDescription)p).OriginalName, Type = p.Type, Value = p.DefaultValue }).ToArray();
            }

            public IVLNodeDescription NodeDescription => description;

            public IVLPin[] Inputs { get; }

            public IVLPin[] Outputs { get; }

            public void Update()
            {
                if (!Inputs.Any())
                    return;

                if ((bool)Inputs.Last().Value)
                {
                    var httpRequest = (HttpWebRequest)WebRequest.CreateHttp(description.QueryUrl);
                    httpRequest.Method = "POST";
                    httpRequest.Accept = "application/json";
                    httpRequest.ContentType = "application/json";
                    if (!string.IsNullOrWhiteSpace(description.FToken))
                        httpRequest.Headers.Add("Authorization", "Bearer " + description.FToken);
                    var inputs = "{";
                    foreach (var input in Inputs.Cast<MyPin>().SkipLast(1))
                    {
                        if (input.Type == typeof(IImage))
                        {
                            inputs += "\"" + input.OriginalName + "\": \"data:image/jpeg;base64,";
                            var skImage = Imaging.FromImage((IImage)input.Value, false);
                            var jpg = skImage.Encode(SKEncodedImageFormat.Jpeg, 100).ToArray();
                            var base64 = Convert.ToBase64String(jpg);
                            inputs += base64 + "\", ";
                        }
                        else if (input.Type == typeof(Spread<float>)) //vector
                        {
                            var v = (Spread<float>)input.Value;
                            inputs += "\"" + input.OriginalName + "\": [" + string.Join(", ", v) + "], ";
                        }
                        else
                            inputs += "\"" + input.OriginalName + "\": " + GetValue(input.Value.ToString()) + ", ";
                    }
                    inputs = inputs.TrimEnd(new char[2] { ',', ' ' }) + "}";
                    var data = Encoding.UTF8.GetBytes(inputs);
                    httpRequest.GetRequestStream().Write(data, 0, data.Length);

                    var rstream = httpRequest.GetResponse().GetResponseStream();
                    string modelInfo = "";
                    using (var reader = new StreamReader(rstream, Encoding.UTF8, true, 0x1000, leaveOpen: true))
                    {
                        modelInfo = reader.ReadToEnd();
                    }
                    dynamic model = JsonConvert.DeserializeObject(modelInfo);

                    foreach (var output in Outputs.Cast<MyPin>())
                    {
                        if (output.Type == typeof(string))
                            output.Value = model[output.OriginalName].ToString();
                        else if (output.Type == typeof(int))
                            output.Value = (int)model[output.OriginalName];
                        else if (output.Type == typeof(float))
                            output.Value = (float)model[output.OriginalName];
                        else if (output.Type == typeof(IEnumerable<RectangleF>)) //array
                        {
                            var rects = new List<RectangleF>();
                            foreach (var rect in model[output.OriginalName])
                            {
                                var x = (float)rect[0];
                                var y = (float)rect[1];
                                var width = (float)rect[2] - x;
                                var height = (float)rect[3] - y;
                                rects.Add(new RectangleF(x, y, width, height));
                            }
                            output.Value = (IEnumerable<RectangleF>)rects;
                        }
                        else if (output.Type == typeof(IEnumerable<float>))
                        {
                            var floats = new List<float>();
                            foreach (var f in model[output.OriginalName])
                                floats.Add((float)f);
                            output.Value = (IEnumerable<float>)floats;
                        }
                        else if (output.Type == typeof(IEnumerable<string>))
                        {
                            var texts = new List<string>();
                            foreach (var t in model[output.OriginalName])
                                texts.Add((string)t);
                            output.Value = (IEnumerable<string>)texts;
                        }
                        else if (output.Type == typeof(IImage))
                        {
                            string result = model[output.OriginalName].ToString();
                            var base64 = result.Split(',').LastOrDefault();
                            var jpg = Convert.FromBase64String(base64);
                            var skImage = SKImage.FromEncodedData(jpg);
                            output.Value = Imaging.ToImage(skImage);
                        }
                        else if (output.Type == typeof(IEnumerable<IEnumerable<Vector2>>))
                        {
                            var landmarks = new List<List<Vector2>>();
                            foreach (var lm in model[output.OriginalName])
                            {
                                var points = new List<Vector2>();
                                foreach (var p in lm)
                                    points.Add(new Vector2((float)p[0], (float)p[1]));
                                landmarks.Add(points);
                            }
                            output.Value = landmarks;
                        }
                    }
                }
            }

            string GetValue(string v)
            {
                if (int.TryParse(v, out int i))
                    return v;
                else if (float.TryParse(v, out float f))
                    return v;
                else if (bool.TryParse(v, out bool b))
                    return v.ToLower();
                else //string
                    return "\"" + v + "\"";
            }

            public void Dispose()
            {
            }
        }
    }
}

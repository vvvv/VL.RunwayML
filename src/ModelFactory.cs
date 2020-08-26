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

// Tells VL what node factory to instantiate. A VL file referencing this assembly will have access to the nodes returned by the factory.
[assembly: NodeFactory(typeof(RunwayMLFactory))]

namespace VL.RunwayML
{
    public class RunwayMLFactory : IVLNodeDescriptionFactory
    {
        public RunwayMLFactory()
        {
            var builder = ImmutableArray.CreateBuilder<IVLNodeDescription>();
            var runway = File.ReadAllText(Path.Combine(Session.UserDocumentFolder, "runway.txt"));

            var hostedModels = runway.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var model in hostedModels)
            {
                var infos = model.Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).ToList();
                builder.Add(new ModelDescription(this, infos[0], infos[1], infos[2]));
            }
            NodeDescriptions = builder.ToImmutable();
        }

        public ImmutableArray<IVLNodeDescription> NodeDescriptions { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        class ModelPinDescription : IVLPinDescription
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

            public ModelPinDescription(string name, Type type, object defaultValue)
            {
                Name = BeautifyPin(name);
                OriginalName = name;
                Type = type;
                DefaultValue = defaultValue;
            }

            public string Name { get; }
            public string OriginalName { get; }
            public Type Type { get; }
            public object DefaultValue { get; }
        }

        class ModelDescription : IVLNodeDescription
        {
            bool initialized;
            bool notFound;

            string Url;
            string FullName;
            string InfoUrl => Url + "info";
            public string QueryUrl => Url + "query";
            public string Token;
            List<ModelPinDescription> inputs = new List<ModelPinDescription>();
            List<ModelPinDescription> outputs = new List<ModelPinDescription>();

            public ModelDescription(IVLNodeDescriptionFactory factory, string name, string url, string token)
            {
                Factory = factory;
                FullName = name;
                Name = name.Split('/').Last();
                Url = url;
                Token = token;
            }

            void Init()
            {
                if (initialized)
                    return;

                try
                {
                    var httpRequest = (HttpWebRequest)WebRequest.CreateHttp(InfoUrl);
                    httpRequest.Accept = "application/json";
                    httpRequest.ContentType = "application/json";
                    httpRequest.Headers.Add("Authorization", "Bearer " + Token);
                    var rstream = httpRequest.GetResponse().GetResponseStream();
                    string modelInfo = "";
                    using (var reader = new StreamReader(rstream, Encoding.UTF8, true, 0x1000, leaveOpen: true))
                    {
                        modelInfo = reader.ReadToEnd();
                    }
                    dynamic model = JsonConvert.DeserializeObject(modelInfo);

                    Type type = typeof(object);
                    object dflt = "";
                    string name = "";
                    foreach (var input in model.inputs)
                    {
                        GetTypeAndDefault(input, ref type, ref dflt);
                        inputs.Add(new ModelPinDescription(input.name.ToString(), type, dflt));
                    }

                    inputs.Add(new ModelPinDescription("Query", typeof(bool), false));

                    foreach (var output in model.outputs)
                    {
                        GetTypeAndDefault(output, ref type, ref dflt);
                        outputs.Add(new ModelPinDescription(output.name.ToString(), type, dflt));
                    }

                    initialized = true;
                    notFound = false;
                }
                catch (Exception e)
                {
                    if (e.Message.Contains("(404)"))
                        notFound = true;
                }
            }

            void GetTypeAndDefault(dynamic pin, ref Type type, ref object dflt)
            {
                if (pin.type == "text")
                {
                    type = typeof(string);
                    dflt = pin.@default.ToString();
                }
                else if (pin.type == "number")
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
                else if (pin.type == "image")
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
                    if (notFound)
                        yield return new Message(MessageType.Warning, "Not found: " + Url);
                    else
                        yield break;
                }
            }

            public IVLNode CreateInstance(NodeContext context)
            {
                return new MyNode(this, context);
            }

            public bool OpenEditor()
            {
                Process.Start("https://app.runwayml.com/models/" + FullName);
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
                    httpRequest.Headers.Add("Authorization", "Bearer " + description.Token);
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
                    inputs = inputs.TrimEnd(new char[2]{ ',', ' '}) + "}";
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
                        else if (output.Type == typeof(IImage))
                        {
                            string result = model[output.OriginalName].ToString();
                            var base64 = result.Split(',').LastOrDefault();
                            var jpg = Convert.FromBase64String(base64);
                            var skImage = SKImage.FromEncodedData(jpg);
                            output.Value = Imaging.ToImage(skImage);
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
                else //string
                    return "\"" + v + "\"";
            }

            public void Dispose()
            {
            }
        }
    }
}

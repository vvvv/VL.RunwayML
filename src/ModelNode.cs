using System.Net;
using System.Text;
using VL.Core;
using Newtonsoft.Json;
using VL.Lib.Basics.Imaging;
using VL.Skia;
using SkiaSharp;
using VL.Lib.Collections;
using Stride.Core.Mathematics;
using System.Reactive.Linq;

namespace VL.RunwayML
{
    sealed class ModelNode : FactoryBasedVLNode, IVLNode
    {
        sealed class Pin : IVLPin
        {
            public Pin(string name, string originalName, Type type)
            {
                Type = type;
                Name = name;
                OriginalName = originalName;
            }

            public object? Value { get; set; }
            public Type Type { get; }
            public string Name { get; }
            public string OriginalName { get; }
        }

        readonly ModelDescription description;

        public ModelNode(ModelDescription description, NodeContext nodeContext) : base(nodeContext)
        {
            this.description = description;
            Inputs = description.Inputs.Select(p => new Pin(p.Name, p.OriginalName, p.Type) { Value = p.DefaultValue }).ToArray();
            Outputs = description.Outputs.Select(p => new Pin(p.Name, p.OriginalName, p.Type) { Value = p.DefaultValue }).ToArray();
        }

        public IVLNodeDescription NodeDescription => description;

        public IVLPin[] Inputs { get; }

        public IVLPin[] Outputs { get; }

        public void Update()
        {
            if (!Inputs.Any())
                return;

            if (Inputs.Last().Value is bool query && query)
            {
                var httpRequest = (HttpWebRequest)WebRequest.CreateHttp(description.QueryUrl);
                httpRequest.Method = "POST";
                httpRequest.Accept = "application/json";
                httpRequest.ContentType = "application/json";
                if (!string.IsNullOrWhiteSpace(description.FToken))
                    httpRequest.Headers.Add("Authorization", "Bearer " + description.FToken);
                var inputs = "{";
                foreach (var input in Inputs.Cast<Pin>().SkipLast(1))
                {
                    if (input.Type == typeof(IImage))
                    {
                        inputs += "\"" + input.OriginalName + "\": \"data:image/jpeg;base64,";
                        var skImage = Imaging.FromImage((IImage)input.Value!, false);
                        var jpg = skImage.Encode(SKEncodedImageFormat.Jpeg, 100).ToArray();
                        var base64 = Convert.ToBase64String(jpg);
                        inputs += base64 + "\", ";
                    }
                    else if (input.Type == typeof(Spread<float>)) //vector
                    {
                        var v = (Spread<float>)input.Value!;
                        inputs += "\"" + input.OriginalName + "\": [" + string.Join(", ", v) + "], ";
                    }
                    else
                        inputs += "\"" + input.OriginalName + "\": " + GetValue(input.Value?.ToString()) + ", ";
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
                dynamic? model = JsonConvert.DeserializeObject(modelInfo);

                if (model is null)
                    return;

                foreach (var output in Outputs.Cast<Pin>())
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
                        if (base64 != null) 
                        {
                            var jpg = Convert.FromBase64String(base64);
                            var skImage = SKImage.FromEncodedData(jpg);
                            output.Value = Imaging.ToImage(skImage);
                        }
                        else
                        {
                            output.Value = null;
                        }
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

        string GetValue(string? v)
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

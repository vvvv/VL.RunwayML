using VL.Core;

namespace VL.RunwayML
{
    sealed class ModelPinDescription : IVLPinDescription, IInfo
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
}

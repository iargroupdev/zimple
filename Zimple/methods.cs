using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zimple
{
    public class MethodParameter
    {
        public string Name { get; set; }
        public Type ParameterType { get; set; }
    }

    public class MethodDescription
    {
        public string MethodName { get; set; }
        public List<MethodParameter> Parameters { get; set; }
    }



}

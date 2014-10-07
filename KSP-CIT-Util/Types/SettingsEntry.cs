using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CIT_Util.Types
{
    public class SettingsEntry
    {
        public object DefaultValue { get; private set; }
        public object Value { get; set; }

        public SettingsEntry(object defaultValue)
        {
            this.DefaultValue = defaultValue;
        }
    }
}

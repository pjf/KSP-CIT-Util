﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CIT_Util
{
    public static class OSD
    {
        public static void PostMessageUpperCenter(string text, float shownFor = 3.7f)
        {
            ScreenMessages.PostScreenMessage(text, shownFor, ScreenMessageStyle.UPPER_CENTER);
        }

        public static void PostMessageLowerRightCorner(string text, float shownFor = 1f)
        {
            ScreenMessages.PostScreenMessage(text, shownFor);
        }
    }
}

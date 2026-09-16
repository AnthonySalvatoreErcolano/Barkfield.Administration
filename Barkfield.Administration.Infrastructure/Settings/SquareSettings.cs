using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Infrastructure.Settings
{
    public class SquareSettings
    {
        public const string SectionName = "Square";
        public string AccessToken { get; set; } = string.Empty;
        public string Environment { get; set; } = "Sandbox"; // "Sandbox" or "Production"
    }
}

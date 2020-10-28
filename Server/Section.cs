using System;
using System.Collections.Generic;
using System.Text;

namespace Server
{
    public class Section
    {
        //A section is meant to be used in the category context but in a broader spectrum.
        //When a customer enters the food Section, only food shops & products will appear.
        //Different from a category, a category is within a section
        //Example: Customer enters the food section, and then enters the chicken category.

        public string id;
        public string title;
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Server
{
    public class Product
    {
        public string id { get; set; }
        public string itemname { get; set; }
        public string description { get; set; }
        public float itemprice { get; set; }
        public string[] pictureids { get; set; }
        public float discountpercent { get; set; }
        public string[] categoriesid { get; set; }
        public string shopid { get; set; }

    }
}

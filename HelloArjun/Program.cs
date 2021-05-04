using System;

namespace HelloArjun
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("How wide is the area to be paved in metres?");
            float width = float.Parse(Console.ReadLine());

            Console.WriteLine("How long is the area to be paved in metres?");
            float length = float.Parse(Console.ReadLine());

            Console.WriteLine("Area to be paved {0:n2} m2", width * length);
 
            Console.WriteLine("How wide is a brick in cm?");
            float ou = float.Parse(Console.ReadLine())/100.0f;

            Console.WriteLine("How long is a brick in cm?");
            float asd = float.Parse(Console.ReadLine())/100.0f;

            Console.WriteLine("The area of a brick is {0:n2} m2", ou * asd);

            Console.WriteLine("you will need {0:n2} bricks", (width * length) / (ou * asd));

        }
    }
}

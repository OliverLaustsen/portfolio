namespace RayTracer.Entities
module Colour =

    exception ColourException

    [<Sealed>]
    type Colour =
      static member ( + ) : Colour * Colour -> Colour
      static member ( * ) : Colour * Colour -> Colour
      static member ( * ) : float * Colour -> Colour
  
    val mkColour : float -> float -> float -> Colour
    val getR : Colour -> float
    val getG : Colour -> float
    val getB : Colour -> float
    val scale: Colour -> float -> Colour
    val merge : float -> Colour -> Colour -> Colour
    val toColor : Colour -> System.Drawing.Color
    val fromColor : System.Drawing.Color -> Colour

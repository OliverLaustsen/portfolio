namespace RayTracer.Entities
open RayTracer.Entities.Point
open RayTracer.Entities.Colour

module Light = 

    type Light = 
     | Light of Point * Colour * float

    let mkLight position colour brightness = Light(position,colour,brightness)
    let getPos (Light(position,_,_)) = position
    let getColour (Light(_,colour,_)) = colour
    let getBrightness (Light(_,_,brightness)) = brightness
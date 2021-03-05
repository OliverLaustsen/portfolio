namespace RayTracer.Entities
open RayTracer.Entities.Point

module BoundingBox =

    type BoundingBox =
        
        //Minimum of x,y,z values of the boundingbox
        abstract member lPoint : Point
        //Maximum of x,y,z values of the boundingbox
        abstract member hPoint : Point
        

    let mkBoundingBox lp hp = 
        {   new BoundingBox
            with member this.lPoint = lp
                 member this.hPoint = hp 
        }

    let isInside (b1:BoundingBox) (b2:BoundingBox) =
        let lx1 = getX b1.lPoint
        let ly1 = getY b1.lPoint
        let lz1 = getZ b1.lPoint

        let hx1 = getX b1.hPoint
        let hy1 = getY b1.hPoint
        let hz1 = getZ b1.hPoint

        let lx2 = getX b2.lPoint
        let ly2 = getY b2.lPoint
        let lz2 = getZ b2.lPoint

        let hx2 = getX b2.hPoint
        let hy2 = getY b2.hPoint
        let hz2 = getZ b2.hPoint

        //Optimize this comparison, atm includes all shapes in all leafs...
        if (lx1 > lx2 && lx1 < hx2 || hx1 > lx2 && hx1 < hx2) || (ly1 > ly2 && ly1 < hy2 || hy1 > ly2 && hy1 < hy2) || (lz1 > lz2 && lz1 < hz2 || hz1 > lz2 && hz1 < hz2) then           
            true
        else false

    let makeCornerPoints (low: Point) (high:Point) = 
        let lx = Point.getX low
        let ly = Point.getY low
        let lz = Point.getZ low

        let hx = Point.getX high
        let hy = Point.getY high
        let hz = Point.getZ high

        let one =   mkPoint lx ly lz
        let two =   mkPoint lx ly hz
        let three = mkPoint lx hy lz
        let four =  mkPoint lx hy hz
        let five =  mkPoint hx ly lz
        let six =   mkPoint hx ly hz
        let seven = mkPoint hx hy lz
        let eight = mkPoint hx hy hz
        [one;two;three;four;five;six;seven;eight]

    let getUnion (b1:BoundingBox) (b2:BoundingBox) = 
        let lx1 = Point.getX b1.lPoint
        let ly1 = Point.getY b1.lPoint
        let lz1 = Point.getZ b1.lPoint
        let lx2 = Point.getX b2.lPoint
        let ly2 = Point.getY b2.lPoint
        let lz2 = Point.getZ b2.lPoint

        let hx1 = Point.getX b1.hPoint
        let hy1 = Point.getY b1.hPoint
        let hz1 = Point.getZ b1.hPoint
        let hx2 = Point.getX b2.hPoint
        let hy2 = Point.getY b2.hPoint
        let hz2 = Point.getZ b2.hPoint

        let lx = min lx1 lx2
        let ly = min ly1 ly2
        let lz = min lz1 lz2

        let hx = max hx1 hx2
        let hy = max hy1 hy2
        let hz = max hz1 hz2
        
        mkBoundingBox (mkPoint lx ly lz) (mkPoint hx hy hz)

    let getCornerPoints (boundingbox:BoundingBox) = 
        let low = boundingbox.lPoint
        let high = boundingbox.hPoint
        makeCornerPoints low high

    let makeBoundingBoxFromCorners (corners:Point list) = 
        if (not (corners.Length = 8)) then 
            failwith "a boundingbox should have 8 corners"
        else
            let mutable lx = infinity
            let mutable ly = infinity
            let mutable lz = infinity

            let mutable hx = -infinity
            let mutable hy = -infinity
            let mutable hz = -infinity

            for corner in corners do 
                let px = Point.getX corner
                let py = Point.getY corner
                let pz = Point.getZ corner
            
                if px < lx then lx <- px
                if py < ly then ly <- py
                if pz < lz then lz <- pz

                if px > hx then hx <- px
                if py > hy then hy <- py
                if pz > hz then hz <- pz

            let l = mkPoint lx ly lz
            let h = mkPoint hx hy hz
            mkBoundingBox l h

        
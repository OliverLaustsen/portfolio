namespace RayTracer.Entities

open Material
open Vector

module Hit =
        type Hit

        val mkHit : float -> Vector -> Material -> Hit
        val mkEmptyHit : Hit
        val getT : Hit -> float
        val getNormal : Hit -> Vector
        val getMaterial : Hit -> Material

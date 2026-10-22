' Deterministic raster/VIEW PRINT/CLS smoke: run with --dialect qbasic.
SCREEN 9
COLOR 1,0
LOCATE 2,2
PRINT "A";
AssertEqual 1,POINT(10,14)
AssertEqual 0,POINT(9,14)
PSET (1,1),2
PSET (1,15),2
PSET (1,29),2
VIEW PRINT 2 TO 2
CLS 2
AssertEqual 2,POINT(1,1)
AssertEqual 0,POINT(1,15)
AssertEqual 2,POINT(1,29)
PRINT "A";
AssertEqual 1,POINT(2,14)
CLS 1
AssertEqual 0,POINT(2,14)
PRINT "B";
AssertEqual 1,POINT(9,14)
VIEW PRINT
CLS 0
AssertEqual 0,POINT(1,1)
AssertEqual 0,POINT(9,14)
SCREEN 0
WIDTH 8,5
LOCATE 1,1
PRINT "Raster smoke passed"

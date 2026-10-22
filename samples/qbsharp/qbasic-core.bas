' Run with --dialect qbasic. Modern QBS intentionally does not parse this syntax.
DEFINT A-Z
DECLARE SUB AddOne (n AS INTEGER)
DECLARE FUNCTION Twice# (n AS INTEGER)
TYPE Person
    Name AS STRING * 17
    Score AS INTEGER
END TYPE
DIM SHARED total
DIM people(1 TO 2) AS Person
FOR i = 1 TO 3
    AddOne total
NEXT i
people(1).Name = "Gorillas"
people(1).Score = total
IF total = 3 THEN result$ = "ok" ELSE result$ = "bad"
SELECT CASE total
    CASE 1 TO 2: result$ = "bad"
    CASE IS = 3: GOSUB MarkDone
    CASE ELSE: result$ = "bad"
END SELECT
DATA 42, "banana"
READ number, word$
RESTORE
READ again
Assert total = 3
Assert result$ = "ok"
Assert people(1).Score = 3
Assert RTRIM$(people(1).Name) = "Gorillas"
Assert number = 42
Assert word$ = "banana"
Assert again = 42
Assert Twice#(total) = 6
PRINT "QBasic core OK"
GOTO Finished
MarkDone:
    result$ = "ok"
RETURN
Finished:
SUB AddOne (n AS INTEGER)
    n = n + 1
END SUB
FUNCTION Twice# (n AS INTEGER)
    Twice# = n * 2
END FUNCTION

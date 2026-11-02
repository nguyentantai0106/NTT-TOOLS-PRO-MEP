(setq ent (entnext))
(setq minX 1e9 minY 1e9 maxX -1e9 maxY -1e9)
(while ent
  (setq elist (entget ent))
  (setq etype (cdr (assoc 0 elist)))
  (if (or (= etype "LINE") (= etype "CIRCLE") (= etype "ARC") (= etype "LWPOLYLINE") (= etype "TEXT"))
    (progn
      (setq pt (cdr (assoc 10 elist)))
      (if pt
        (progn
          (setq minX (min minX (car pt)))
          (setq maxX (max maxX (car pt)))
          (setq minY (min minY (cadr pt)))
          (setq maxY (max maxY (cadr pt)))
        )
      )
    )
  )
  (setq ent (entnext ent))
)
(princ (strcat "\nBOUNDS: X=[" (rtos minX 2 2) ".." (rtos maxX 2 2) "] (W=" (rtos (- maxX minX) 2 2) ") Y=[" (rtos minY 2 2) ".." (rtos maxY 2 2) "] (H=" (rtos (- maxY minY) 2 2) ")"))
(command "QUIT" "_Y")

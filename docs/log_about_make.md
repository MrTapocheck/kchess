В Services/ChessAI.cs добавил полноценный make/unmake для поиска с учетом:

+ en-passant
    + корректное снятие пешки “с прохода” при make
    + корректное восстановление при unmake
    + учет _enPassantTarget в состоянии поиска
+ рокировки
    + перемещение ладьи при make
    + откат ладьи при unmake
    + генерация рокировок в поиске (O-O и O-O-O) с проверками битых полей и флагов
+ флаги состояния
    + сохранение/восстановление _whiteKingMoved, _blackKingMoved, _whiteRookKingsideMoved, _whiteRookQueensideMoved, _blackRookKingsideMoved, _blackRookQueensideMoved
+ обновление флагов при ходе короля/ладьи и при взятии ладьи на стартовой клетке

Также поправил генерацию ходов в дереве:

+ добавил en-passant и рокировку в GenerateAllLegalMovesFast(...)
+ проверка легальности теперь идет через ApplyMoveForSearch(...) / UnmakeMoveForSearch(...), а не через “голую” перестановку фигур.

И да — про GPU помню:

+ GPU-инференс через ONNX Runtime CUDA сохранился
+ батч-оценка на depth == 1 (EvaluateBatch) осталась и продолжает использоваться, теперь уже поверх более корректного состояния позиции.

___
23.03.26
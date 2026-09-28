/// ntl_love_event(op, ...) —— love.event 最小兼容
var _op = string_lower(string(argument[0]));

// love.event.quit() —— NTL 里不真的退出（避免 mod 崩掉游戏）
if (_op == "quit") { ntl_log("love", "[event] quit() 被忽略（Neutraled 不允许 mod 退出游戏）"); return undefined; }

// love.event.push / pump —— 空实现
if (_op == "push" || _op == "pump" || _op == "clear") return undefined;

// love.event.poll —— 返回空
if (_op == "poll") return undefined;

return undefined;

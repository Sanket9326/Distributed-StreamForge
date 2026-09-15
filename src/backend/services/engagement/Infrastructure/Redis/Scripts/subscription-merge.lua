-- @common
-- @subscription
expect(KEYS[1], 'hash'); expect(KEYS[2], 'zset'); expect(KEYS[3], 'string'); expect(KEYS[4], 'string')
if (#ARGV - 1) % 2 ~= 0 or (#ARGV - 1) / 2 > 500 then error('INVALID_BATCH') end
if redis.call('GET', KEYS[3]) ~= ARGV[1] or redis.call('GET', KEYS[4]) ~= ARGV[1] then return 0 end
local prepared = {}
for i = 2, #ARGV, 2 do
  local old = redis.call('HGET', KEYS[1], ARGV[i]); old = old and decode_state(old)
  prepared[#prepared + 1] = {id = ARGV[i], old = old, state = choose(old, decode_state(ARGV[i + 1]))}
end
for _, item in ipairs(prepared) do write_state(KEYS[1], KEYS[2], item.id, item.old, item.state) end
return 1

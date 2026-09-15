-- @common
-- @subscription
expect(KEYS[1], 'hash'); expect(KEYS[2], 'zset'); expect(KEYS[3], 'hash'); expect(KEYS[4], 'zset')
local incoming = decode_state(ARGV[3])
local a = redis.call('HGET', KEYS[1], ARGV[1]); local b = redis.call('HGET', KEYS[3], ARGV[2])
a = a and decode_state(a); b = b and decode_state(b)
local result = choose(a, choose(b, incoming))
write_state(KEYS[1], KEYS[2], ARGV[1], a, result)
write_state(KEYS[3], KEYS[4], ARGV[2], b, result)
return cjson.encode(result)

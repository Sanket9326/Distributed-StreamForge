-- @common
-- @history
expect(KEYS[1], 'hash'); expect(KEYS[2], 'zset'); expect(KEYS[3], 'string')
local incoming = history_decode(ARGV[2])
local result = history_write(KEYS[1], KEYS[2], ARGV[1], incoming)
redis.call('DEL', KEYS[3])
return cjson.encode(result)

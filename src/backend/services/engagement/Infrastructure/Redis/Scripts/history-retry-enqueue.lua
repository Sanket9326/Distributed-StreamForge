-- @common
expect(KEYS[1], 'zset'); expect(KEYS[2], 'hash')
if redis.call('HEXISTS', KEYS[2], ARGV[1]) == 1 then return 1 end
local due = tonumber(ARGV[3]); if not due then error('INVALID_DUE') end
redis.call('HSET', KEYS[2], ARGV[1], ARGV[2])
redis.call('ZADD', KEYS[1], due, ARGV[2])
return 1

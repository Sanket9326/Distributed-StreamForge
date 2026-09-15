-- @common
expect(KEYS[1], 'string')
local window = tonumber(ARGV[1]); local limit = tonumber(ARGV[2])
if not window or window <= 0 or not limit or limit <= 0 then error('INVALID_LIMIT') end
local current = redis.call('GET', KEYS[1])
if current then decimal(current) end
local n = redis.call('INCR', KEYS[1])
if n == 1 then redis.call('EXPIRE', KEYS[1], window) end
if n > limit then return redis.call('TTL', KEYS[1]) end
return 0

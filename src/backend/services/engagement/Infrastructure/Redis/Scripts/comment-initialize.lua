-- @common
expect(KEYS[1], 'string'); expect(KEYS[2], 'string')
local count = decimal(ARGV[2])
if redis.call('GET', KEYS[2]) ~= ARGV[1] then return false end
redis.call('SET', KEYS[1], count, 'NX')
return redis.call('GET', KEYS[1])

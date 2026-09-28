-- @common
expect(KEYS[1], 'hash'); expect(KEYS[2], 'string')
local current = redis.call('HGET', KEYS[1], ARGV[1])
if current then return current end
redis.call('SET', KEYS[2], 'absent', 'EX', 30)
return 'absent'
